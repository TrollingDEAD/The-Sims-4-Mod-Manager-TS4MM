using Sims4ModManager.Core.Tray;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.App.ViewModels;

/// <summary>One library entry with its detected custom content, formatted for display.</summary>
public sealed partial class TrayItemViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public TrayItemViewModel(TrayItem item, IReadOnlyList<TrayCcReference> cc)
    {
        Model = item;
        Cc = cc.Select(r => new TrayCcReferenceViewModel(r)).ToList();

        var metadata = item.Metadata;
        TypeLabel = FormatType(item.Type);
        Name = item.DisplayName;
        Creator = metadata?.CreatorName ?? string.Empty;
        Description = metadata?.Description ?? string.Empty;
        SimsLabel = metadata is null || metadata.Sims.Count == 0 ? string.Empty : string.Join(", ", metadata.Sims.Select(s => s.FullName));
        DateLabel = metadata?.CreatedUtc?.ToLocalTime().ToString("d") ?? item.LastWriteUtc.ToLocalTime().ToString("d");
        SortDate = metadata?.CreatedUtc ?? item.LastWriteUtc;

        var details = new List<string>();
        if (metadata?.LotSize is { } size) details.Add(L.F("Grundstück {0}×{1}", size.Width, size.Depth));
        if (metadata?.Sims.Count > 0) details.Add(L.F("{0} Sim(s)", metadata.Sims.Count));
        details.Add(L.F("{0} Datei(en), {1}", item.Files.Count, FormatSize(item.TotalSizeBytes)));
        if (metadata is { Downloads: > 0 }) details.Add(L.F("{0} Downloads, {1} Favoriten", metadata.Downloads, metadata.Favorites));
        DetailsLabel = string.Join(" · ", details);

        // Gallery tags come as "a,b,c" - show them as hashtags so they can wrap between words.
        var tags = (metadata?.Tags ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        TagsLabel = tags.Count == 0
            ? string.Empty
            : string.Join(" ", tags.Take(12).Select(t => "#" + t)) + (tags.Count > 12 ? $" … (+{tags.Count - 12})" : "");

        DisabledCcCount = cc.Count(r => !r.IsEnabled);
        CcLabel = cc.Count == 0 ? "–" : DisabledCcCount == 0 ? cc.Count.ToString() : L.F("{0} ({1} aus)", cc.Count, DisabledCcCount);

        IsComplete = item.IsComplete;
        StatusLabel = item.IsComplete
            ? (DisabledCcCount > 0 ? L.T("CC deaktiviert") : "OK")
            : L.T("Unvollständig");
        ProblemsLabel = string.Join(Environment.NewLine, item.Problems);
        FileNames = item.Files.Select(System.IO.Path.GetFileName).ToList()!;
    }

    public TrayItem Model { get; }
    public IReadOnlyList<TrayCcReferenceViewModel> Cc { get; }

    public string TypeLabel { get; }
    public string Name { get; }
    public string Creator { get; }
    public string Description { get; }
    public string SimsLabel { get; }
    public string DateLabel { get; }
    public DateTime SortDate { get; }
    public string DetailsLabel { get; }
    public string TagsLabel { get; }
    public int DisabledCcCount { get; }
    public string CcLabel { get; }
    public bool IsComplete { get; }
    public string StatusLabel { get; }
    public bool HasDisabledCc => DisabledCcCount > 0;
    public string ProblemsLabel { get; }
    public IReadOnlyList<string> FileNames { get; }

    /// <summary>From the game index: packs the item needs, and content that is nowhere to be found.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string packsLabel = string.Empty;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string missingLabel = string.Empty;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool hasMissing;

    public void ApplyGameCheck(TrayGameCheck check)
    {
        string packs = string.Join(", ", check.RequiredPacks.Select(p => p.DisplayName));
        if (check.PacksComplete)
            PacksLabel = check.RequiredPacks.Count == 0 ? L.T("Benötigte Packs: nur Basisspiel") : L.F("Benötigte Packs: {0}", packs);
        else
            PacksLabel = check.RequiredPacks.Count == 0
                ? L.T("Benötigte Packs: bei Grundstücken und Räumen nicht sicher prüfbar")
                : L.F("Benötigte Packs (mindestens): {0}", packs);
        HasMissing = check.MissingCount > 0;
        if (!check.MissingCheckSupported)
            MissingLabel = Model.Type == TrayItemType.Household ? string.Empty : L.T("Fehlender CC: wird nur bei Haushalten geprüft");
        else if (check.MissingCount == 0)
            MissingLabel = L.T("Fehlender CC: keiner – alles vorhanden");
        else
            MissingLabel = L.F("Nicht gefunden: {0} Teil(e) ({1}) – weder in den Mods noch im Spiel. Fehlender CC oder ein nicht installiertes Pack.",
                check.MissingCount, string.Join(", ", check.MissingCategories));
    }

    /// <summary>Text the search box matches against.</summary>
    public string SearchText => $"{Name} {Creator} {SimsLabel} {Description} {Model.Metadata?.Tags}";

    internal static string FormatType(TrayItemType type) => type switch
    {
        TrayItemType.Household => L.T("Haushalt"),
        TrayItemType.Lot => L.T("Grundstück"),
        TrayItemType.Room => L.T("Raum"),
        _ => L.T("Unbekannt")
    };

    internal static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.#} {units[unit]}";
    }
}

public sealed class TrayCcReferenceViewModel
{
    public TrayCcReferenceViewModel(TrayCcReference reference)
    {
        Model = reference;
        ModName = reference.Mod.DisplayName;
        FileLabel = reference.Mod.IsFolder ? reference.File.RelativePathInMod : string.Empty;
        CategoriesLabel = string.Join(", ", reference.Categories);
        CountLabel = reference.ResourceCount.ToString();
        StatusLabel = reference.IsEnabled ? L.T("Aktiv") : L.T("Deaktiviert");
        IsEnabled = reference.IsEnabled;
    }

    public TrayCcReference Model { get; }
    public string ModName { get; }
    public string FileLabel { get; }
    public string CategoriesLabel { get; }
    public string CountLabel { get; }
    public string StatusLabel { get; }
    public bool IsEnabled { get; }
}
