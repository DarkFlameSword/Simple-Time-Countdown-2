using System.Windows;

namespace TimeCountdown.Controls;

/// <summary>
/// Placeholder text for the themed text fields (TextBox.Field and derived styles), shown while
/// the field is empty. Placeholders only suggest content; every field still has a visible label
/// and an AutomationProperties.Name.
/// </summary>
public static class Hint
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text",
        typeof(string),
        typeof(Hint),
        new FrameworkPropertyMetadata(string.Empty));

    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);
}
