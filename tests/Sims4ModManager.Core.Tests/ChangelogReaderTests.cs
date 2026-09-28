using Sims4ModManager.Core.Updates;

namespace Sims4ModManager.Core.Tests;

public class ChangelogReaderTests
{
    [Fact]
    public void SectionForReturnsOnlyThatVersionsBody()
    {
        string? section = ChangelogReader.SectionFor("1.0.1");

        Assert.NotNull(section);
        Assert.Contains("English is now the app's default UI language", section);
        // Must not bleed into neighboring sections.
        Assert.DoesNotContain("Initial public release", section);
        Assert.DoesNotContain("Self-updating", section);
    }

    [Fact]
    public void SectionForReturnsNullForAnUnknownVersion()
    {
        Assert.Null(ChangelogReader.SectionFor("99.99.99"));
    }

    [Fact]
    public void ToPlainTextStripsMarkdownSyntax()
    {
        string plain = ChangelogReader.ToPlainText(
            "### Added\n\n- Uses [Velopack](https://velopack.io) and `dotnet run`.\n- Another line.");

        Assert.DoesNotContain("###", plain);
        Assert.DoesNotContain("`", plain);
        Assert.DoesNotContain("[Velopack]", plain);
        Assert.Contains("Velopack (https://velopack.io)", plain);
        Assert.Contains("• Uses", plain);
    }
}
