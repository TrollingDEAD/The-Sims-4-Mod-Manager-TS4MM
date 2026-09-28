using System.Globalization;
using System.Text.RegularExpressions;
using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Conflicts;

public enum ResolutionKind
{
    /// <summary>The same file is installed twice (usually in different folders or versions).</summary>
    DuplicateFile,
    /// <summary>Two versions of the same mod (v1/v2, UPDATE, "(1)" copies ...).</summary>
    OlderVersion,
    /// <summary>HQ and NonHQ variant of the same download - only one is meant to be installed.</summary>
    QualityVariant,
    /// <summary>Every resource of one file is also in another file (typically a MERGED set).</summary>
    ContainedInOtherFile,
    /// <summary>Two different script mods ship the same Python module.</summary>
    ScriptOverlap,
    /// <summary>Different mods override the same resources - a winner has to be picked.</summary>
    Override
}

public enum ResolutionAction
{
    /// <summary>Rename the file to .disabled (nothing is deleted).</summary>
    DisableFile,
    /// <summary>Rewrite the package without the conflicting resources; the rest of the mod stays.</summary>
    RemoveResources
}

/// <summary>A proposed fix for (part of) a conflict group. Applying it is journaled and can be undone.</summary>
public sealed class ResolutionProposal
{
    public required ResolutionKind Kind { get; init; }
    public required ResolutionAction Action { get; init; }
    public required string Title { get; init; }
    public required string Explanation { get; init; }

    /// <summary>The file that stays untouched and "wins".</summary>
    public required ConflictSource Keep { get; init; }

    /// <summary>The file that gets disabled or loses the conflicting resources.</summary>
    public required ConflictSource Change { get; init; }

    /// <summary>Resources removed from <see cref="Change"/> (only for <see cref="ResolutionAction.RemoveResources"/>).</summary>
    public IReadOnlySet<ResourceKey> Keys { get; init; } = new HashSet<ResourceKey>();

    /// <summary>The first proposal for a file pair is the recommended one; alternatives follow.</summary>
    public bool IsRecommended { get; init; } = true;

    /// <summary>
    /// Safe proposals only disable a file whose content is duplicated elsewhere (duplicate, older
    /// version, quality variant, contained in a merged set) - they can be applied in bulk.
    /// </summary>
    public bool IsSafe => IsRecommended && Action == ResolutionAction.DisableFile
                          && Kind is not (ResolutionKind.ScriptOverlap or ResolutionKind.Override);

    public string ActionLabel => Action == ResolutionAction.DisableFile
        ? L.F("{0} deaktivieren", ConflictResolver.Label(Change))
        : L.F("{0} Ressource(n) aus {1} entfernen", Keys.Count, ConflictResolver.Label(Change));
}

public sealed record ResolutionResult(bool Success, string Message);

/// <summary>
/// Suggests and applies fixes for conflicts, preferring solutions that keep mods installed:
/// duplicates and superseded files are disabled (renamed, not deleted), real overrides between
/// different mods are fixed by removing only the contested resources from the losing package.
/// Works on files, not mods, since one folder "mod" can contain hundreds of unrelated packages.
/// </summary>
public static class ConflictResolver
{
    private sealed class PairInfo
    {
        public required ConflictSource A { get; init; }
        public required ConflictSource B { get; init; }
        public HashSet<ResourceKey> Keys { get; } = new();
        public HashSet<string> Modules { get; } = new(StringComparer.Ordinal);
        public HashSet<string> TypeNames { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Proposals for the whole report. Computed globally (not per group) so a file pair that appears
    /// in several groups gets one proposal covering all its shared resources, and so every file is
    /// paired with its single best partner.
    /// </summary>
    public static IReadOnlyList<ResolutionProposal> ProposeAll(ConflictReport report) =>
        Propose(report.Groups.SelectMany(g => g.Items));

    /// <summary>The proposals from <see cref="ProposeAll"/> that touch files of <paramref name="group"/>.</summary>
    public static IReadOnlyList<ResolutionProposal> ForGroup(IEnumerable<ResolutionProposal> all, ConflictGroup group)
    {
        var files = group.Items.SelectMany(i => i.Sources).Select(s => s.File.AbsolutePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return all.Where(p => files.Contains(p.Change.File.AbsolutePath) && files.Contains(p.Keep.File.AbsolutePath)).ToList();
    }

    public static IReadOnlyList<ResolutionProposal> Propose(IEnumerable<ConflictItem> items)
    {
        var pairs = new Dictionary<(string, string), PairInfo>();

        foreach (var item in items)
        {
            var sources = item.Sources.DistinctBy(s => s.File.AbsolutePath, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < sources.Count; i++)
            {
                for (int j = i + 1; j < sources.Count; j++)
                {
                    if (ReferenceEquals(sources[i].Mod, sources[j].Mod))
                        continue; // same mod: not a conflict

                    var (a, b) = string.CompareOrdinal(sources[i].File.AbsolutePath, sources[j].File.AbsolutePath) < 0
                        ? (sources[i], sources[j]) : (sources[j], sources[i]);
                    var id = (a.File.AbsolutePath, b.File.AbsolutePath);
                    if (!pairs.TryGetValue(id, out var pair))
                        pairs[id] = pair = new PairInfo { A = a, B = b };

                    pair.TypeNames.Add(item.TypeName);
                    if (item.Key is { } key)
                        pair.Keys.Add(key);
                    if (item.ModulePath is { } module)
                        pair.Modules.Add(module);
                }
            }
        }

        // Related files (duplicates, versions, merged sets, ...) are always paired correctly.
        var proposals = new List<ResolutionProposal>();
        var relatedPairs = new HashSet<PairInfo>();
        foreach (var pair in pairs.Values)
        {
            var related = ProposeForRelatedPair(pair).ToList();
            if (related.Count == 0)
                continue;
            proposals.AddRange(related);
            relatedPairs.Add(pair);
        }

        // Real overrides: a resource shared by N files of one mod and M files of another would give
        // N×M pairs (e.g. a creator's string table in every package of two copies of a set). Each file
        // gets exactly one best partner - related files first, then most shared resources, then the most
        // similar name - and only mutual best partners become an override proposal.
        var disabled = proposals.Where(p => p.IsRecommended && p.Action == ResolutionAction.DisableFile)
            .Select(p => p.Change.File.AbsolutePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var bestPartner = pairs.Values
            .SelectMany(p => new[] { (File: p.A, Other: p.B, Pair: p), (File: p.B, Other: p.A, Pair: p) })
            .GroupBy(x => x.File.File.AbsolutePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => relatedPairs.Contains(x.Pair))
                      .ThenByDescending(x => x.Pair.Keys.Count + x.Pair.Modules.Count)
                      .ThenByDescending(x => CommonPrefixLength(BaseName(x.File.File), BaseName(x.Other.File)))
                      .ThenBy(x => x.Other.File.AbsolutePath, StringComparer.OrdinalIgnoreCase)
                      .First().Pair,
                StringComparer.OrdinalIgnoreCase);

        foreach (var pair in pairs.Values.Where(p => !relatedPairs.Contains(p) && p.Keys.Count > 0))
        {
            string a = pair.A.File.AbsolutePath, b = pair.B.File.AbsolutePath;
            if (disabled.Contains(a) || disabled.Contains(b))
                continue;
            if (!ReferenceEquals(bestPartner[a], pair) || !ReferenceEquals(bestPartner[b], pair))
                continue;
            proposals.AddRange(ProposeOverride(pair));
        }

        return proposals
            .DistinctBy(p => (p.Action, p.Change.File.AbsolutePath, p.Keep.File.AbsolutePath))
            .ToList();
    }

    /// <summary>All safe proposals across the report, at most one per file to change.</summary>
    public static IReadOnlyList<ResolutionProposal> SafeProposals(ConflictReport report)
    {
        var proposals = ProposeAll(report).Where(p => p.IsSafe).ToList();
        var toChange = proposals.Select(p => p.Change.File.AbsolutePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Never disable a file that another proposal relies on as the surviving copy.
        return proposals
            .Where(p => !toChange.Contains(p.Keep.File.AbsolutePath))
            .DistinctBy(p => p.Change.File.AbsolutePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static ResolutionResult Apply(ResolutionProposal proposal, ChangeRecorder recorder)
    {
        var file = proposal.Change.File;
        try
        {
            if (proposal.Action == ResolutionAction.DisableFile)
            {
                var result = ModToggleService.SetFileEnabled(file, enable: false, recorder);
                return result.Success
                    ? new ResolutionResult(true, L.F("{0} deaktiviert.", Label(proposal.Change)))
                    : new ResolutionResult(false, string.Join("; ", result.Failures.Select(f => f.Message)));
            }

            int expected = file.Resources.Count(r => !proposal.Keys.Contains(r.Key));
            recorder.Replace(file.AbsolutePath, temp =>
            {
                DbpfWriter.WriteWithout(file.AbsolutePath, proposal.Keys, temp);
                int written = DbpfReader.TryReadIndex(temp)?.Count ?? -1;
                if (written != expected)
                    throw new InvalidDataException($"Überprüfung fehlgeschlagen: {written} statt {expected} Ressourcen geschrieben.");
            });
            return new ResolutionResult(true, L.F("{0} Ressource(n) aus {1} entfernt.", proposal.Keys.Count, Label(proposal.Change)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new ResolutionResult(false, $"{Label(proposal.Change)}: {ex.Message}");
        }
    }

    /// <summary>Display label of a file: "Mod › relative\path.package" for folder mods, the file name otherwise.</summary>
    public static string Label(ConflictSource source) =>
        source.Mod.IsFolder
            ? $"{source.Mod.DisplayName} › {ModFileNaming.GetEffectiveFileName(source.File.RelativePathInMod)}"
            : ModFileNaming.GetEffectiveFileName(source.File.RelativePathInMod);

    /// <summary>Proposals for files that belong together (same file, versions, variants, merged set, scripts); empty otherwise.</summary>
    private static IEnumerable<ResolutionProposal> ProposeForRelatedPair(PairInfo pair)
    {
        var a = pair.A;
        var b = pair.B;
        string nameA = BaseName(a.File);
        string nameB = BaseName(b.File);

        // 1. Same file installed twice.
        if (string.Equals(nameA, nameB, StringComparison.OrdinalIgnoreCase))
        {
            var (keep, change) = PreferNewer(a, b);
            yield return Disable(ResolutionKind.DuplicateFile, keep, change,
                L.T("Doppelt installierte Datei"),
                L.F("„{0}“ ist zweimal installiert ({1} und {2}). ", nameA, Label(a), Label(b)) +
                L.F("Die neuere Kopie ({0}) bleibt aktiv, die ältere ({1}) wird deaktiviert.", Date(keep), Date(change)));
            yield break;
        }

        // 2. HQ + NonHQ variant of the same download.
        var variantA = ParseVariant(nameA);
        var variantB = ParseVariant(nameB);
        if (variantA.Core.Length > 0 && variantA.Core == variantB.Core
            && (variantA.IsNonHq != variantB.IsNonHq || variantA.HasHq != variantB.HasHq))
        {
            bool aIsBetter = variantA.IsNonHq != variantB.IsNonHq ? !variantA.IsNonHq : variantA.HasHq;
            var (keep, change) = aIsBetter ? (a, b) : (b, a);
            yield return Disable(ResolutionKind.QualityVariant, keep, change,
                L.T("HQ- und NonHQ-Variante gleichzeitig installiert"),
                L.F("Die HQ-Variante ({0}) und die NonHQ-Variante ({1}) sind Alternativen – ", Label(keep), Label(change)) +
                L.T("es sollte nur eine installiert sein. Die HQ-Variante bleibt aktiv (für schwächere PCs stattdessen die NonHQ-Variante behalten)."));
            yield return Disable(ResolutionKind.QualityVariant, change, keep,
                L.T("HQ- und NonHQ-Variante gleichzeitig installiert"), L.T("Alternative: NonHQ-Variante behalten."), recommended: false);
            yield break;
        }

        // 3. Two versions of the same mod.
        if (variantA.VersionCore.Length > 0 && variantA.VersionCore == variantB.VersionCore && variantA.Core != variantB.Core)
        {
            var (keep, change) = PreferHigherVersion(a, variantA, b, variantB);
            yield return Disable(ResolutionKind.OlderVersion, keep, change,
                L.T("Zwei Versionen desselben Mods"),
                L.F("{0} und {1} sind Versionen desselben Mods. ", Label(a), Label(b)) +
                L.F("Die neuere ({0}) bleibt aktiv, die ältere ({1}) wird deaktiviert.", Label(keep), Label(change)));
            yield break;
        }

        // 4. One file is fully contained in the other (merged set + single item).
        var keysA = a.File.Resources.Select(r => r.Key).ToHashSet();
        var keysB = b.File.Resources.Select(r => r.Key).ToHashSet();
        if (keysA.Count > 0 && keysB.Count > 0 && keysA.Count != keysB.Count)
        {
            var (small, big, smallKeys, bigKeys) = keysA.Count < keysB.Count ? (a, b, keysA, keysB) : (b, a, keysB, keysA);
            if (smallKeys.IsSubsetOf(bigKeys))
            {
                yield return Disable(ResolutionKind.ContainedInOtherFile, big, small,
                    L.T("Bereits in einem anderen Package enthalten"),
                    L.F("Alle {0} Ressourcen von {1} stecken auch in {2} ", smallKeys.Count, Label(small), Label(big)) +
                    L.F("({0} Ressourcen, z. B. ein MERGED-Set). {1} ist damit überflüssig und wird deaktiviert.", bigKeys.Count, Label(small)));
                yield break;
            }
        }

        // 5. Script modules: archives are not edited, the older one is disabled.
        if (pair.Modules.Count > 0)
        {
            var (keep, change) = PreferNewer(a, b);
            yield return Disable(ResolutionKind.ScriptOverlap, keep, change,
                L.T("Skript-Mods mit gleichen Python-Modulen"),
                L.F("{0} und {1} enthalten {2} gleiche Python-Modul(e) ", Label(a), Label(b), pair.Modules.Count) +
                L.F("(z. B. {0}). Meist ist eines davon eine veraltete Kopie oder eine mitgelieferte Bibliothek. ", pair.Modules.First()) +
                L.F("Vorschlag: die ältere Datei ({0}, {1}) deaktivieren.", Label(change), Date(change)));
        }

    }

    /// <summary>Real override between different mods: pick a winner, strip only the contested resources from the loser.</summary>
    private static IEnumerable<ResolutionProposal> ProposeOverride(PairInfo pair)
    {
        string types = string.Join(", ", pair.TypeNames.OrderBy(t => t, StringComparer.CurrentCulture));
        var (keep, change) = PreferNewer(pair.A, pair.B);
        yield return Strip(keep, change, pair.Keys, types, recommended: true);
        yield return Strip(change, keep, pair.Keys, types, recommended: false);
    }

    private static ResolutionProposal Strip(ConflictSource keep, ConflictSource change, HashSet<ResourceKey> keys, string types, bool recommended)
    {
        bool removesEverything = change.File.Resources.All(r => keys.Contains(r.Key));
        string title = recommended ? L.T("Mods überschreiben dieselben Ressourcen") : L.T("Alternative: andere Version gewinnt");

        if (removesEverything)
            return Disable(ResolutionKind.Override, keep, change, title,
                L.F("{0} besteht nur aus den umstrittenen Ressourcen ({1}) und wird deshalb deaktiviert; {2} gewinnt.", Label(change), types, Label(keep)),
                recommended);

        return new ResolutionProposal
        {
            Kind = ResolutionKind.Override,
            Action = ResolutionAction.RemoveResources,
            Title = title,
            Explanation = recommended
                ? L.F("{0} und {1} ändern dieselben {2} Ressourcen ({3}); im Spiel kann nur eine Version wirken. ", Label(keep), Label(change), keys.Count, types) +
                  L.F("Vorschlag: die neuere Datei ({0}, {1}) gewinnt – die {2} Ressourcen werden aus {3} entfernt, ", Label(keep), Date(keep), keys.Count, Label(change)) +
                  L.T("der Rest dieses Mods bleibt erhalten.")
                : L.F("Stattdessen {0} gewinnen lassen und die {1} Ressourcen aus {2} entfernen.", Label(keep), keys.Count, Label(change)),
            Keep = keep,
            Change = change,
            Keys = keys,
            IsRecommended = recommended
        };
    }

    private static ResolutionProposal Disable(ResolutionKind kind, ConflictSource keep, ConflictSource change,
        string title, string explanation, bool recommended = true) => new()
    {
        Kind = kind,
        Action = ResolutionAction.DisableFile,
        Title = title,
        Explanation = explanation,
        Keep = keep,
        Change = change,
        IsRecommended = recommended
    };

    private static (ConflictSource Keep, ConflictSource Change) PreferNewer(ConflictSource a, ConflictSource b)
    {
        var dateA = LastWrite(a);
        var dateB = LastWrite(b);
        if (dateA != dateB)
            return dateA > dateB ? (a, b) : (b, a);
        return a.File.Resources.Count >= b.File.Resources.Count ? (a, b) : (b, a);
    }

    /// <summary>
    /// Newer version by name first (v2 &gt; v1, "fixed"/"update" &gt; plain &gt; "old"), file date only as a
    /// fallback - copying files around resets their date, so it is the least reliable signal.
    /// </summary>
    private static (ConflictSource Keep, ConflictSource Change) PreferHigherVersion(
        ConflictSource a, NameVariant variantA, ConflictSource b, NameVariant variantB)
    {
        if (variantA.Version is { } va && variantB.Version is { } vb && Math.Abs(va - vb) > 1e-9)
            return va > vb ? (a, b) : (b, a);
        if (variantA.RevisionScore != variantB.RevisionScore)
            return variantA.RevisionScore > variantB.RevisionScore ? (a, b) : (b, a);
        return PreferNewer(a, b);
    }

    private static DateTime LastWrite(ConflictSource source)
    {
        try { return File.GetLastWriteTimeUtc(source.File.AbsolutePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    private static string Date(ConflictSource source) => LastWrite(source).ToLocalTime().ToString("d", CultureInfo.CurrentCulture);

    private static int CommonPrefixLength(string a, string b)
    {
        int n = 0;
        while (n < a.Length && n < b.Length && char.ToLowerInvariant(a[n]) == char.ToLowerInvariant(b[n]))
            n++;
        return n;
    }

    private static string BaseName(ModFileInfo file) =>
        Path.GetFileNameWithoutExtension(ModFileNaming.GetEffectiveFileName(Path.GetFileName(file.AbsolutePath)));

    // --- Name analysis ------------------------------------------------------------------------

    private static readonly Regex QualityToken = new(@"(?<![a-z0-9])(non[\s_\-]?hq|hq)(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NonHqToken = new(@"(?<![a-z0-9])non[\s_\-]?hq(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VersionToken = new(@"(?<![a-z0-9])(v|ver|version)[\s_\-\.]?(?<num>\d+(\.\d+)?)(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RevisionWords = new(@"(?<![a-z0-9])(updated?|remaster(ed)?|fixed|final|new|old|kopie|copy)(?![a-z0-9])|\(\d+\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Separators = new(@"[\s_\-\.\(\)\[\]]+", RegexOptions.Compiled);

    /// <param name="Core">Name without quality markers (HQ/NonHQ) and separators.</param>
    /// <param name="VersionCore">Core additionally without version/revision markers (v2, UPDATE, "(1)" ...).</param>
    /// <param name="RevisionScore">+1 for "update/fixed/remaster/new/final", -1 for "old"/"copy" markers, 0 otherwise.</param>
    internal readonly record struct NameVariant(string Core, bool IsNonHq, bool HasHq, string VersionCore, double? Version, int RevisionScore);

    private static readonly Regex NewerWords = new(@"(?<![a-z0-9])(updated?|fixed|remaster(ed)?|new|final)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OlderWords = new(@"(?<![a-z0-9])(old|kopie|copy)(?![a-z0-9])|\(\d+\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Splits a file name into its core and its quality/version markers.</summary>
    internal static NameVariant ParseVariant(string name)
    {
        string lower = name.ToLowerInvariant();
        bool nonHq = NonHqToken.IsMatch(lower);
        bool hq = !nonHq && QualityToken.IsMatch(lower);
        string withoutQuality = QualityToken.Replace(lower, " ");
        string core = Separators.Replace(withoutQuality, "");

        var versionMatch = VersionToken.Match(withoutQuality);
        double? version = versionMatch.Success && double.TryParse(versionMatch.Groups["num"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : null;
        string versionCore = Separators.Replace(RevisionWords.Replace(VersionToken.Replace(withoutQuality, " "), " "), "");

        int revision = (NewerWords.IsMatch(lower) ? 1 : 0) - (OlderWords.IsMatch(lower) ? 1 : 0);
        return new NameVariant(core, nonHq, hq, versionCore, version, revision);
    }
}
