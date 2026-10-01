namespace Sims4ModManager.Core.LoadOrder;

/// <summary>
/// Computes the Mods folder's effective file load order. The game scans directories depth-first,
/// merging files and subfolders alphabetically at each level - not "all files first" or "all folders
/// first" as often repeated, but a single alphabetical pass over each directory's entries that
/// recurses into a subfolder fully before moving on to its next sibling. This is also the only model
/// consistent with the community's numeric-prefix convention (000_, 010_, ...), which relies on a
/// folder and a file at the same level being directly comparable by name.
/// <para>
/// The first resource the game reads for a given key wins; anything providing the same key later in
/// the scan is ignored (not merged). So a file's rank here doubles as "how likely it is to win a
/// resource override", lowest rank first.
/// </para>
/// </summary>
public static class LoadOrderCalculator
{
    /// <summary>
    /// Ranks every file <paramref name="isTracked"/> accepts, 0-based, in true scan order. Other
    /// directory entries (readmes, disabled files, unrelated content) still occupy a position in the
    /// alphabetical merge and so influence sibling order, but get no rank of their own.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Rank(string modsRoot, Func<string, bool> isTracked)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int rank = 0;
        Visit(modsRoot);
        return result;

        void Visit(string directory)
        {
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

            foreach (string entry in entries.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                if (Directory.Exists(entry))
                    Visit(entry);
                else if (isTracked(entry))
                    result[entry] = rank++;
            }
        }
    }
}
