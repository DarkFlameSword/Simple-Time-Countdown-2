using System.IO;
using System.Text.RegularExpressions;
using TimeCountdown.Models;
using TimeCountdown.Services;
using Xunit;

namespace TimeCountdown.Tests;

public sealed partial class LocalizationTests
{
    // Values that are intentionally identical in both languages: the brand name, endonyms and
    // pure layout patterns.
    private static readonly HashSet<string> SameInBothLanguages =
    [
        "App.Name", "Window.Main.Title", "Error.Title", "Language.English", "Language.Chinese",
        "Main.Clock", "Editor.Counter", "Time.Pair"
    ];

    private static readonly LocalizationService Localization = LocalizationService.Instance;

    [Fact]
    public void BothLanguagesDefineTheSameKeys()
    {
        var english = Localization.GetKeys(LocalizationService.English).ToHashSet();
        var chinese = Localization.GetKeys(LocalizationService.Chinese).ToHashSet();

        Assert.Empty(english.Except(chinese));
        Assert.Empty(chinese.Except(english));
    }

    [Fact]
    public void ChineseTableIsActuallyTranslated()
    {
        var untranslated = Localization.GetKeys(LocalizationService.Chinese)
            .Where(key => !SameInBothLanguages.Contains(key))
            .Where(key => Localization.GetRaw(LocalizationService.Chinese, key) == Localization.GetRaw(LocalizationService.English, key))
            .ToList();

        Assert.Empty(untranslated);
    }

    [Fact]
    public void FormatPatternsTakeTheSameArgumentsInBothLanguages()
    {
        foreach (var key in Localization.GetKeys(LocalizationService.English))
        {
            var english = Placeholders(Localization.GetRaw(LocalizationService.English, key)!);
            var chinese = Placeholders(Localization.GetRaw(LocalizationService.Chinese, key)!);
            Assert.True(english.SetEquals(chinese), $"{key}: en uses {{{string.Join(",", english)}}}, zh uses {{{string.Join(",", chinese)}}}");
        }
    }

    [Fact]
    public void EveryKeyUsedInXamlOrCodeExists()
    {
        var known = Localization.GetKeys(LocalizationService.English).ToHashSet();
        var missing = ReferencedKeys().Where(key => !known.Contains(key)).Distinct().ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryStatusHasABadge()
    {
        var known = Localization.GetKeys(LocalizationService.English).ToHashSet();

        Assert.All(Enum.GetNames<CountdownStatus>(), status => Assert.Contains($"Status.{status}", known));
    }

    private static HashSet<string> Placeholders(string pattern)
    {
        return PlaceholderPattern().Matches(pattern).Select(match => match.Groups[1].Value).ToHashSet();
    }

    private static IEnumerable<string> ReferencedKeys()
    {
        var separator = Path.DirectorySeparatorChar;
        foreach (var file in Directory.EnumerateFiles(RepoPaths.AppSource, "*.*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                     .Where(path => !path.Contains($"{separator}obj{separator}") &&
                                    !path.Contains($"{separator}bin{separator}") &&
                                    !path.EndsWith("LocalizationService.cs", StringComparison.OrdinalIgnoreCase)))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in XamlKeyPattern().Matches(text))
            {
                yield return match.Groups[1].Value;
            }

            foreach (Match match in CodeKeyPattern().Matches(text))
            {
                yield return match.Groups[1].Value;
            }
        }
    }

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderPattern();

    // {Binding [Main.Search.Label], Source={StaticResource Loc}}
    [GeneratedRegex(@"\[([A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)\]\s*,\s*Source=\{StaticResource Loc\}")]
    private static partial Regex XamlKeyPattern();

    // _localization["Key"], Loc["Key"], _localization.Format("Key", ...)
    [GeneratedRegex(@"(?:localization|Localization\.Instance|\bloc|\bLoc)\s*(?:\[\s*|\.Format\(\s*)""([A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)""")]
    private static partial Regex CodeKeyPattern();
}
