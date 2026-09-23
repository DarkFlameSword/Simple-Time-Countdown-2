using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using ComboBox = System.Windows.Controls.ComboBox;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace TimeCountdown.Controls;

/// <summary>
/// Drag-to-move for the app's chromeless windows, shared so every window applies the same rules.
///
/// A press starts a drag only when it lands on inert surface (paper, text, ornaments), never on
/// something the user operates. The walk up from the hit element handles text content elements
/// (a Run inside a TextBlock is not a Visual, so VisualTreeHelper would throw on it) by stepping
/// through the logical tree until it reaches a Visual again.
/// </summary>
public static class WindowDrag
{
    /// <summary>
    /// Starts <see cref="Window.DragMove"/> for a primary-button press on inert surface.
    /// Safe to call from both tunnelling and bubbling mouse-down handlers.
    /// </summary>
    public static void TryDragMove(Window window, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            e.ButtonState != MouseButtonState.Pressed ||
            e.StylusDevice is not null ||
            IsInteractive(e.OriginalSource as DependencyObject))
        {
            return;
        }

        try
        {
            window.DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove throws if the button was released between the press and this call.
        }
    }

    /// <summary>True when the element or one of its ancestors is an operable control.</summary>
    public static bool IsInteractive(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase or TextBoxBase or PasswordBox or ComboBox or Slider or ScrollBar or Thumb
                or DatePicker or Calendar or ResizeGrip)
            {
                return true;
            }

            source = source is Visual or Visual3D
                ? VisualTreeHelper.GetParent(source) ?? LogicalTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }
}
