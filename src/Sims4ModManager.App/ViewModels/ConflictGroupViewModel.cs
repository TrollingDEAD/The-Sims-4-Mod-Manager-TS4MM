using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.App.ViewModels;

/// <summary>One group of mods that conflict with each other, with all items they collide on.</summary>
public sealed class ConflictGroupViewModel
{
    public ConflictGroupViewModel(ConflictGroup group)
    {
        Model = group;
        Severity = group.Severity;
        SeverityLabel = FormatSeverity(group.Severity);
        ModNamesLabel = string.Join(" ↔ ", group.Mods.Select(m => m.DisplayName));

        // e.g. "12 Konflikte: 8× CAS-Teil, 4× Textur (DDS)"
        var byType = group.Items
            .GroupBy(i => i.TypeName)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count()}× {g.Key}");
        SummaryLabel = L.F("{0} Konflikt(e): {1}", group.Items.Count, string.Join(", ", byType));

        Items = group.Items.Select(i => new ConflictItemViewModel(i)).ToList();
    }

    public ConflictGroup Model { get; }
    public ConflictSeverity Severity { get; }
    public string SeverityLabel { get; }
    public string ModNamesLabel { get; }
    public string SummaryLabel { get; }
    public IReadOnlyList<ConflictItemViewModel> Items { get; }

    internal static string FormatSeverity(ConflictSeverity severity) => severity switch
    {
        ConflictSeverity.High => L.T("Hoch"),
        ConflictSeverity.Medium => L.T("Mittel"),
        _ => L.T("Niedrig")
    };
}

public sealed class ConflictItemViewModel
{
    public ConflictItemViewModel(ConflictItem item)
    {
        Severity = item.Severity;
        SeverityLabel = ConflictGroupViewModel.FormatSeverity(item.Severity);
        SeverityTooltip = L.F("Schwere: {0}", SeverityLabel);
        TypeLabel = item.TypeName;
        KeyLabel = item.Label;
    }

    public ConflictSeverity Severity { get; }
    public string SeverityLabel { get; }
    public string SeverityTooltip { get; }
    public string TypeLabel { get; }
    public string KeyLabel { get; }
}
