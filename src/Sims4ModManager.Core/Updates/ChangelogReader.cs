using System.Reflection;
using System.Text.RegularExpressions;

namespace Sims4ModManager.Core.Updates;

/// <summary>
/// Reads the embedded CHANGELOG.md so the app can show a "what's new" summary after updating,
/// using the same section format the release workflow already parses for GitHub release notes.
/// </summary>
public static class ChangelogReader
{
    /// <summary>The body of the given version's "## [x.y.z]" section (without the heading itself), or null if not found.</summary>
    public static string? SectionFor(string version)
    {
        string text = ReadEmbedded();
        var match = Regex.Match(text,
            $@"^## \[{Regex.Escape(version)}\][^\r\n]*\r?\n(.*?)(?=\r?\n## \[|\z)",
            RegexOptions.Multiline | RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>Light Markdown-to-text cleanup: a MessageBox can't render Markdown, but the raw syntax reads poorly.</summary>
    public static string ToPlainText(string markdown)
    {
        string text = Regex.Replace(markdown, "`([^`]+)`", "$1");
        text = Regex.Replace(text, @"\[([^\]]+)\]\(([^)]+)\)", "$1 ($2)");
        text = Regex.Replace(text, "^### +", "", RegexOptions.Multiline);
        text = Regex.Replace(text, "^- +", "• ", RegexOptions.Multiline);
        return text.Trim();
    }

    private static string ReadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("Sims4ModManager.Core.CHANGELOG.md")
            ?? throw new InvalidOperationException("CHANGELOG.md is not embedded in the Core assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
