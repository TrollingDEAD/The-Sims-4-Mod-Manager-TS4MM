using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Tests;

/// <summary>L is global state: these tests must not run in parallel with tests that check German texts.</summary>
[CollectionDefinition("Localization", DisableParallelization = true)]
public class LocalizationCollection { }

[Collection("Localization")]
public class LocalizationTests
{

    [Fact]
    public void EnglishTranslationCoversFormatsAndFallsBackToGerman()
    {
        try
        {
            L.Use(L.English);
            Assert.Equal("Hair", L.T("Haare"));
            Assert.Equal("Unbekannter Text ohne Übersetzung", L.T("Unbekannter Text ohne Übersetzung"));
            Assert.Equal("3 mods", L.F("{0} Mods", 3));
            L.Use(L.German);
            Assert.Equal("Haare", L.T("Haare"));
        }
        finally
        {
            L.Use(L.German);
        }
    }

    [Fact]
    public void EnglishDictionaryKeepsPlaceholders()
    {
        var english = L.Load(L.English);
        Assert.NotEmpty(english);
        foreach (var (german, text) in english)
        {
            var expected = System.Text.RegularExpressions.Regex.Matches(german, @"\{\d+[^}]*\}").Select(m => m.Value).OrderBy(v => v);
            var actual = System.Text.RegularExpressions.Regex.Matches(text, @"\{\d+[^}]*\}").Select(m => m.Value).OrderBy(v => v);
            Assert.True(expected.SequenceEqual(actual), $"Placeholders differ: „{german}“ → „{text}“");
        }
    }
}
