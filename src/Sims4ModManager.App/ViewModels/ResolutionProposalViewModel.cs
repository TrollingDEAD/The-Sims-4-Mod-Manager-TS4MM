using Sims4ModManager.Core.Conflicts;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.App.ViewModels;

/// <summary>A conflict solution as shown in the conflict panel.</summary>
public sealed class ResolutionProposalViewModel
{
    public ResolutionProposalViewModel(ResolutionProposal proposal)
    {
        Model = proposal;
    }

    public ResolutionProposal Model { get; }

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
