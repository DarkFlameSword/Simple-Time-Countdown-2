using System.Text;

namespace TimeCountdown.Services;

/// <summary>
/// Limits and normalisation for user-entered countdown text. The same rules back the editor's
/// MaxLength values, the save path, and state loading, so an entry can never be longer or
/// shaped differently than the card layout was designed for.
/// </summary>
public static class TextInput
{
    public const int MaxTitleLength = 120;
    public const int MaxNoteLength = 280;
    public const int MaxSearchLength = 100;

    /// <summary>
    /// Collapses newlines, tabs, other control characters and runs of whitespace into single
    /// spaces, trims the ends and truncates to <paramref name="maxLength"/> without splitting a
    /// surrogate pair.
    /// </summary>
    public static string Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return Truncate(builder.ToString(), maxLength);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var cut = maxLength;
        if (char.IsHighSurrogate(value[cut - 1]))
        {
            cut--;
        }

        return value[..cut].TrimEnd();
    }
}
