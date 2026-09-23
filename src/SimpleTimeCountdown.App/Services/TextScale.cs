using Microsoft.Win32;

namespace TimeCountdown.Services;

/// <summary>
/// The Windows "Text size" accessibility setting (Settings › Accessibility › Text size), which
/// WPF does not apply by itself. The factor is folded into every window's UiScale.FontScale, so
/// all theme text grows with it; layouts wrap or scroll rather than clip, so larger text stays
/// fully readable.
/// </summary>
public static class TextScale
{
    private const string AccessibilityKey = @"Software\Microsoft\Accessibility";
    private const string FactorValue = "TextScaleFactor";
    private static double _factor = ReadFactor();
    private static bool _listening;

    /// <summary>Raised on the thread that received the system notification when the factor changes.</summary>
    public static event EventHandler? Changed;

    /// <summary>1.0 at the default text size, up to 2.25 at the largest.</summary>
    public static double Factor
    {
        get
        {
            EnsureListening();
            return _factor;
        }
    }

    private static void EnsureListening()
    {
        if (_listening)
        {
            return;
        }

        _listening = true;
        SystemEvents.UserPreferenceChanged += (_, _) =>
        {
            var factor = ReadFactor();
            if (Math.Abs(factor - _factor) < 0.001)
            {
                return;
            }

            _factor = factor;
            Changed?.Invoke(null, EventArgs.Empty);
        };
    }

    private static double ReadFactor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AccessibilityKey);
            return key?.GetValue(FactorValue) is int percent ? Math.Clamp(percent, 100, 225) / 100.0 : 1.0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return 1.0;
        }
    }
}
