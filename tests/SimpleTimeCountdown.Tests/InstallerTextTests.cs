using System.Text.RegularExpressions;
using TimeCountdown.Setup;
using Xunit;

namespace TimeCountdown.Tests;

public sealed partial class InstallerTextTests
{
    [Fact]
    public void BothLanguagesDefineTheSameKeys()
    {
        Assert.Empty(InstallerText.English.Keys.Except(InstallerText.Chinese.Keys));
        Assert.Empty(InstallerText.Chinese.Keys.Except(InstallerText.English.Keys));
    }

    [Fact]
    public void PlaceholdersMatchInBothLanguages()
    {
        foreach (var (key, english) in InstallerText.English)
        {
            var chinese = InstallerText.Chinese[key];
            Assert.True(
                Placeholders(english).SetEquals(Placeholders(chinese)),
                $"{key}: '{english}' and '{chinese}' take different placeholders");
        }
    }

    [Fact]
    public void AccessKeysAppearInBothLanguagesOrNeither()
    {
        foreach (var (key, english) in InstallerText.English)
        {
            Assert.True(
                HasAccessKey(english) == HasAccessKey(InstallerText.Chinese[key]),
                $"{key}: only one language marks an access key with '&'");
        }
    }

    [Fact]
    public void ChineseTableIsActuallyTranslated()
    {
        var untranslated = InstallerText.English
            .Where(pair => pair.Value.Any(char.IsLetter) && InstallerText.Chinese[pair.Key] == pair.Value)
            .Select(pair => pair.Key)
            .ToList();

        Assert.Empty(untranslated);
    }

    private static HashSet<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text).Select(match => match.Value).ToHashSet();

    // A single '&' before a character marks the access key; '&&' is a literal ampersand.
    private static bool HasAccessKey(string text) => AccessKeyPattern().IsMatch(text);

    [GeneratedRegex(@"\{(?:\d+|product)(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"(?<!&)&(?!&)\S")]
    private static partial Regex AccessKeyPattern();
}
