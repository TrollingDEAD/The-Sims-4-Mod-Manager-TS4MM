using Sims4ModManager.Core.Conflicts;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.App.ViewModels;

/// <summary>A conflict solution as shown in the conflict panel.</summary>
public sealed class ResolutionProposalViewModel
{
    public ResolutionProposalViewModel(ResolutionProposal proposal, IReadOnlyDictionary<string, int>? loadOrderRanks = null)
    {
        Model = proposal;

        // Override/script conflicts are exactly the two kinds ConflictResolver.IsSafe excludes from
        // auto-apply - the two where "which file actually wins" is a real judgment call, and where the
        // game's own "first loaded wins" rule can disagree with this proposal's file-date heuristic.
        if (loadOrderRanks is not null && proposal.Kind is ResolutionKind.Override or ResolutionKind.ScriptOverlap
            && loadOrderRanks.TryGetValue(proposal.Keep.File.AbsolutePath, out int keepRank)
            && loadOrderRanks.TryGetValue(proposal.Change.File.AbsolutePath, out int changeRank)
            && changeRank < keepRank)
        {
            LoadOrderNote = L.F("Nach der tatsächlichen Ladereihenfolge gewinnt heute eigentlich {0}, nicht {1}.",
                ConflictResolver.Label(proposal.Change), ConflictResolver.Label(proposal.Keep));
        }
    }

    public ResolutionProposal Model { get; }

    /// <summary>Set when the game's real load order picks a different winner than this proposal's "Keep" file.</summary>
    public string? LoadOrderNote { get; }
    public bool HasLoadOrderNote => LoadOrderNote is not null;

    public string Title => Model.Title;
    public string Explanation => Model.Explanation;
    public string ActionLabel => Model.ActionLabel;
    public string KindLabel => FormatKind(Model.Kind);
    public bool IsRecommended => Model.IsRecommended;
    public bool IsSafe => Model.IsSafe;

    /// <summary>Rewrites a package (as opposed to only renaming a file).</summary>
    public bool EditsPackage => Model.Action == ResolutionAction.RemoveResources;

    public string BadgeLabel => IsSafe ? L.T("sicher") : IsRecommended ? L.T("empfohlen") : L.T("Alternative");

    public string ActionTooltip => EditsPackage
        ? L.T("Schreibt das Package ohne die umstrittenen Ressourcen neu. Die Originaldatei wird vorher gesichert (Verlauf).")
        : L.T("Benennt die Datei in .disabled um – sie bleibt erhalten und kann jederzeit wieder aktiviert werden.");

    internal static string FormatKind(ResolutionKind kind) => kind switch
    {
        ResolutionKind.DuplicateFile => L.T("Doppelt installierte Dateien"),
        ResolutionKind.OlderVersion => L.T("Ältere Versionen"),
        ResolutionKind.QualityVariant => L.T("NonHQ-Varianten neben HQ"),
        ResolutionKind.ContainedInOtherFile => L.T("Bereits in MERGED-Sets enthalten"),
        ResolutionKind.ScriptOverlap => L.T("Skript-Überschneidungen"),
        _ => L.T("Überschreibungen")
    };
}
