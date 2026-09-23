using TimeCountdown.Services;
using Xunit;

namespace TimeCountdown.Tests;

public sealed class TextInputTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData("  Submit  thesis ", "Submit thesis")]
    [InlineData("line one\r\nline two", "line one line two")]
    [InlineData("tab\tseparated\u0007bell", "tab separated bell")]
    public void Normalize_CollapsesWhitespaceAndControlCharacters(string? input, string expected)
    {
        Assert.Equal(expected, TextInput.Normalize(input, 100));
    }

    [Fact]
    public void Normalize_TruncatesToTheLimit()
    {
        var result = TextInput.Normalize(new string('a', 500), TextInput.MaxTitleLength);

        Assert.Equal(TextInput.MaxTitleLength, result.Length);
    }

    [Fact]
    public void Normalize_NeverSplitsASurrogatePair()
    {
        // 9 ASCII letters then an emoji (two UTF-16 units) straddling a limit of 10.
        var result = TextInput.Normalize("abcdefghi\U0001F382", 10);

        Assert.Equal("abcdefghi", result);
        Assert.False(char.IsHighSurrogate(result[^1]));
    }
}
