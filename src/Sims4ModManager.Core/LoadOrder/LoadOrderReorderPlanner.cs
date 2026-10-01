namespace Sims4ModManager.Core.LoadOrder;

/// <summary>One sibling entry (file or folder) participating in a reorder, in its current real order.</summary>
public sealed record ReorderItem(string Path, string Name, int? PrefixNumber);

/// <summary>A single rename <see cref="LoadOrderReorderPlanner.Plan"/> would make.</summary>
public sealed record ReorderRename(string OldPath, string NewPath);

/// <summary>
/// Plans the renames needed to move one sibling to a new position among others in the same folder,
/// preferring to touch only the moved item (a number strictly between its new neighbors) and falling
/// back to renumbering every sibling with clean, evenly-spaced numbers (000, 010, 020, ...) whenever
/// that isn't safely possible - e.g. no numeric gap between neighbors, or the existing numbers don't
/// already match the siblings' real order (nothing reliable to slot a single new number into).
/// </summary>
public static class LoadOrderReorderPlanner
{
    private const int Gap = 10;

    /// <summary><paramref name="siblings"/> must already be in true current order. Returns no-op for an unchanged position.</summary>
    public static IReadOnlyList<ReorderRename> Plan(IReadOnlyList<ReorderItem> siblings, int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex || fromIndex < 0 || fromIndex >= siblings.Count || toIndex < 0 || toIndex >= siblings.Count)
            return Array.Empty<ReorderRename>();

        var reordered = new List<ReorderItem>(siblings);
        var moved = reordered[fromIndex];
        reordered.RemoveAt(fromIndex);
        reordered.Insert(toIndex, moved);

        if (TryPlanSingleRename(reordered, moved, out var single))
            return single;

        var assignments = reordered.Select((item, i) => (Item: item, Number: i * Gap));
        return BuildRenames(assignments);
    }

    /// <summary>
    /// Only valid if every OTHER sibling's existing prefix numbers already strictly increase in their
    /// (unchanged) relative order - otherwise there is nothing reliable to slot a single new number
    /// into, and a full renumber is the safe choice instead.
    /// </summary>
    private static bool TryPlanSingleRename(List<ReorderItem> reordered, ReorderItem moved, out IReadOnlyList<ReorderRename> renames)
    {
        renames = Array.Empty<ReorderRename>();
        var others = reordered.Where(i => !ReferenceEquals(i, moved)).ToList();
        for (int i = 1; i < others.Count; i++)
        {
            if (others[i - 1].PrefixNumber is not { } prev || others[i].PrefixNumber is not { } next || next <= prev)
                return false;
        }

        int movedIndex = reordered.IndexOf(moved);
        int? prevNumber = movedIndex > 0 ? reordered[movedIndex - 1].PrefixNumber : null;
        int? nextNumber = movedIndex < reordered.Count - 1 ? reordered[movedIndex + 1].PrefixNumber : null;

        int low = prevNumber ?? -1;
        int high = nextNumber ?? (low + Gap * 2 + 1);
        if (high - low < 2)
            return false;

        renames = BuildRenames(new[] { (Item: moved, Number: low + (high - low) / 2) });
        return true;
    }

    private static IReadOnlyList<ReorderRename> BuildRenames(IEnumerable<(ReorderItem Item, int Number)> assignments)
    {
        var renames = new List<ReorderRename>();
        foreach (var (item, number) in assignments)
        {
            if (item.PrefixNumber == number)
                continue;
            string rest = LoadOrderNaming.StripPrefix(item.Name);
            string newName = LoadOrderNaming.FormatWithPrefix(number, rest);
            if (string.Equals(newName, item.Name, StringComparison.Ordinal))
                continue;
            string newPath = Path.Combine(Path.GetDirectoryName(item.Path)!, newName);
            renames.Add(new ReorderRename(item.Path, newPath));
        }
        return renames;
    }
}
