using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Persistence;

namespace Sims4ModManager.Core.Catalog;

/// <summary>What the catalog knows about one mod file (cached by path, size and date).</summary>
public sealed class PackageCatalogInfo
{
    /// <summary>Enabled path in lower case, so toggling a mod does not invalidate the cache.</summary>
    public string Key { get; set; } = string.Empty;
    public long Size { get; set; }
    public long WriteTicks { get; set; }

    public ContentCategory Category { get; set; }
    public CasCategory CasCategory { get; set; }

    /// <summary>In-game name from the package's string table (objects, some CAS parts).</summary>
    public string? Name { get; set; }

    /// <summary>Internal names of the first CAS parts (they often start with the creator's name).</summary>
    public List<string> CasPartNames { get; set; } = new();

    /// <summary>Number of CAS parts or catalog objects (swatches/variants).</summary>
    public int ItemCount { get; set; }

    /// <summary>Union of the CAS parts' age/gender flags.</summary>
    public uint AgeGender { get; set; }

    /// <summary>File name of the extracted preview image in the thumbnail cache, if the package has one.</summary>
    public string? Thumbnail { get; set; }

    /// <summary>Meshes this file's CAS parts or objects use but does not contain itself (recolors).</summary>
    public List<ExternalMesh> ExternalMeshes { get; set; } = new();
}

/// <summary>A mesh a recolor needs from elsewhere: the item's name and the acceptable keys (one per level of detail).</summary>
public sealed class ExternalMesh
{
    public string Item { get; set; } = string.Empty;
    public List<string> Keys { get; set; } = new();

    public IEnumerable<ResourceKey> ParsedKeys => Keys.Select(ResourceKey.TryParse).OfType<ResourceKey>();
}

/// <summary>
/// Reads catalog details (category, CAS area, names, age/gender, preview image) from mod files.
/// Results are cached in %AppData%\Sims4ModManager\cache, so only new or changed files are read.
/// </summary>
public sealed class CatalogScanner
{
    private const int CacheVersion = 2;
    private const int MaxCasPartsRead = 60;
    private const int MaxThumbnailBytes = 400_000;

    private const uint CasThumbnail = 0x3C1AF1F2;
    private const uint ObjectThumbnail = 0x3C2A8647;
    private const uint OtherThumbnail = 0x5B282D45;

    private sealed class CacheFile
    {
        public int Version { get; set; }
        public List<PackageCatalogInfo> Entries { get; set; } = new();
    }

    private readonly string _cacheFile;
    private ConcurrentDictionary<string, PackageCatalogInfo>? _cache;

    public CatalogScanner(string? cacheRoot = null)
    {
        string root = cacheRoot ?? AppPaths.Cache;
        _cacheFile = Path.Combine(root, "catalog.json");
        ThumbnailDirectory = Path.Combine(root, "thumbs");
    }

    public string ThumbnailDirectory { get; }

    /// <summary>String table language to prefer for names (0x00 English, 0x08 German).</summary>
    public byte PreferredLanguage { get; set; } = 0x08;

    public static string KeyOf(string path) => ModFileNaming.ToEnabledPath(path).ToLowerInvariant();

    /// <summary>Catalog details for every file of <paramref name="mods"/>; reads (in parallel) only what is not cached yet.</summary>
    public IReadOnlyDictionary<ModFileInfo, PackageCatalogInfo> Scan(
        IEnumerable<ModEntry> mods, IProgress<(int Done, int Total)>? progress = null, CancellationToken cancel = default)
    {
        var cache = LoadCache();
        var files = mods.SelectMany(m => m.Files).ToList();
        var result = new ConcurrentDictionary<ModFileInfo, PackageCatalogInfo>();
        var missing = new List<ModFileInfo>();

        foreach (var file in files)
        {
            if (cache.TryGetValue(KeyOf(file.AbsolutePath), out var cached)
                && cached.Size == file.SizeBytes && cached.WriteTicks == file.LastWriteUtc.Ticks
                && (cached.Thumbnail is null || File.Exists(Path.Combine(ThumbnailDirectory, cached.Thumbnail))))
                result[file] = cached;
            else
                missing.Add(file);
        }

        int done = files.Count - missing.Count;
        progress?.Report((done, files.Count));
        if (missing.Count > 0)
        {
            Directory.CreateDirectory(ThumbnailDirectory);
            Parallel.ForEach(missing, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancel }, file =>
            {
                var info = Read(file);
                result[file] = info;
                cache[info.Key] = info;
                int now = Interlocked.Increment(ref done);
                if (now % 50 == 0)
                    progress?.Report((now, files.Count));
            });
            progress?.Report((files.Count, files.Count));
            SaveCache(cache, files);
        }
        return result;
    }

    private PackageCatalogInfo Read(ModFileInfo file)
    {
        var info = new PackageCatalogInfo
        {
            Key = KeyOf(file.AbsolutePath),
            Size = file.SizeBytes,
            WriteTicks = file.LastWriteUtc.Ticks,
            Category = ModClassifier.Classify(file)
        };
        if (file.Kind != ModFileKind.Package || file.Resources.Count == 0)
            return info;

        var casParts = file.Resources.Where(r => r.Key.Type == CasPartReader.ResourceType).ToList();
        var objects = file.Resources.Where(r => r.Key.Type == CatalogObjectReader.ResourceType).ToList();
        info.ItemCount = casParts.Count > 0 ? casParts.Count : objects.Count;

        var definitions = file.Resources.Where(r => r.Key.Type == MeshReferenceReader.ObjectDefinition).ToList();
        var wanted = casParts.Concat(objects.Take(3)).Concat(definitions).ToList(); // all CAS parts: every recolor needs its mesh
        var thumbnail = PickThumbnail(file.Resources, casParts.FirstOrDefault());
        if (thumbnail is { } t)
            wanted.Add(t);
        var tables = file.Resources.Where(r => r.Key.Type == StringTableReader.ResourceType)
            .OrderBy(r => StringTableReader.Language(r.Key.Instance) == PreferredLanguage ? 0 : StringTableReader.Language(r.Key.Instance) == 0 ? 1 : 2)
            .Take(1).ToList();
        wanted.AddRange(tables);

        var data = DbpfReader.TryReadResources(file.AbsolutePath, wanted);

        var parsed = casParts.Take(MaxCasPartsRead)
            .Select(r => data.TryGetValue(r, out var bytes) ? CasPartReader.TryRead(bytes) : null)
            .OfType<CasPartInfo>().ToList();
        if (parsed.Count > 0)
        {
            info.CasCategory = parsed.GroupBy(p => ContentCategories.FromBodyType(p.BodyType))
                .OrderByDescending(g => g.Count()).First().Key;
            info.AgeGender = parsed.Aggregate(0u, (acc, p) => acc | p.AgeGender);
            info.CasPartNames = parsed.Select(p => p.Name).Where(n => n.Length > 0).Distinct().Take(5).ToList();
        }
        else if (info.Category == ContentCategory.Cas && file.Resources.Any(r => r.Key.Type == 0x0354796A))
        {
            info.CasCategory = CasCategory.SkinTone;
        }

        var strings = tables.Count > 0 && data.TryGetValue(tables[0], out var table)
            ? StringTableReader.TryRead(table)
            : new Dictionary<uint, string>();
        uint? nameKey = objects.Count > 0 && data.TryGetValue(objects[0], out var cobj)
            ? CatalogObjectReader.TryReadNameKey(cobj)
            : parsed.Select(p => p.TitleKey).FirstOrDefault(k => k != 0);
        if (nameKey is uint key && strings.TryGetValue(key, out var name))
            info.Name = CleanName(name);

        info.ExternalMeshes = FindExternalMeshes(file, casParts, definitions, data);

        if (thumbnail is { } thumb && data.TryGetValue(thumb, out var image) && image.Length is > 64 and <= MaxThumbnailBytes)
        {
            string fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{info.Key}|{info.Size}|{info.WriteTicks}")))[..20] + ".img";
            try
            {
                File.WriteAllBytes(Path.Combine(ThumbnailDirectory, fileName), image);
                info.Thumbnail = fileName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // no preview then
            }
        }
        return info;
    }

    /// <summary>Recolors: CAS parts whose GEOM keys and objects whose model all lie outside this package.</summary>
    private static List<ExternalMesh> FindExternalMeshes(ModFileInfo file, List<PackageResource> casParts,
        List<PackageResource> definitions, IReadOnlyDictionary<PackageResource, byte[]> data)
    {
        var own = file.Resources.Select(r => r.Key).ToHashSet();
        var result = new Dictionary<ResourceKey, ExternalMesh>();
        foreach (var part in casParts)
        {
            if (!data.TryGetValue(part, out var bytes))
                continue;
            var meshes = MeshReferenceReader.ReadCasMeshes(bytes);
            if (meshes.Count == 0 || meshes.Any(own.Contains) || result.ContainsKey(meshes[0]))
                continue;
            result[meshes[0]] = new ExternalMesh
            {
                Item = CasPartReader.TryRead(bytes)?.Name ?? string.Empty,
                Keys = meshes.Distinct().Take(6).Select(k => k.ToString()).ToList()
            };
        }
        foreach (var definition in definitions)
        {
            if (!data.TryGetValue(definition, out var bytes))
                continue;
            var (model, name) = MeshReferenceReader.ReadObject(bytes);
            if (model is { } key && !own.Contains(key) && !result.ContainsKey(key))
                result[key] = new ExternalMesh { Item = name ?? string.Empty, Keys = new List<string> { key.ToString() } };
        }
        return result.Values.ToList();
    }

    /// <summary>Strips markup ("&lt;font color=…&gt;") and the search keywords creators append after "|".</summary>
    public static string? CleanName(string raw)
    {
        string text = System.Text.RegularExpressions.Regex.Replace(raw, "<[^>]*>", string.Empty);
        int bar = text.IndexOf('|');
        if (bar > 0)
            text = text[..bar];
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Medium-size preview: the CAS thumbnail of the first CAS part, else a buy/build thumbnail.</summary>
    private static PackageResource? PickThumbnail(IReadOnlyList<PackageResource> resources, PackageResource firstCasPart)
    {
        var cas = resources.Where(r => r.Key.Type == CasThumbnail).ToList();
        if (cas.Count > 0)
        {
            return cas.Where(r => r.Key.Instance == firstCasPart.Key.Instance).OrderBy(r => r.MemorySize).Cast<PackageResource?>().FirstOrDefault()
                   ?? cas.OrderBy(r => r.MemorySize).First();
        }

        var objects = resources.Where(r => r.Key.Type == ObjectThumbnail).ToList();
        if (objects.Count > 0)
        {
            int[] preference = { 2, 1, 3, 0 }; // size class in the low bits of the group
            return objects.OrderBy(r => Array.IndexOf(preference, (int)(r.Key.Group & 0xF)) is var i && i >= 0 ? i : 9)
                .ThenBy(r => r.Key.Instance).First();
        }

        return resources.Where(r => r.Key.Type == OtherThumbnail).OrderByDescending(r => r.MemorySize).Cast<PackageResource?>().FirstOrDefault();
    }

    private ConcurrentDictionary<string, PackageCatalogInfo> LoadCache()
    {
        if (_cache is not null)
            return _cache;
        var file = JsonFile.TryRead<CacheFile>(_cacheFile);
        var entries = file?.Version == CacheVersion ? file.Entries : new List<PackageCatalogInfo>();
        _cache = new ConcurrentDictionary<string, PackageCatalogInfo>(
            entries.GroupBy(e => e.Key).Select(g => KeyValuePair.Create(g.Key, g.Last())));
        return _cache;
    }

    /// <summary>Keeps only entries of files that still exist and removes orphaned preview images.</summary>
    private void SaveCache(ConcurrentDictionary<string, PackageCatalogInfo> cache, IReadOnlyList<ModFileInfo> current)
    {
        var live = current.Select(f => KeyOf(f.AbsolutePath)).ToHashSet();
        foreach (string key in cache.Keys.Where(k => !live.Contains(k)).ToList())
            cache.TryRemove(key, out _);

        try
        {
            JsonFile.WriteAtomic(_cacheFile, new CacheFile { Version = CacheVersion, Entries = cache.Values.ToList() });
            var used = cache.Values.Select(v => v.Thumbnail).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string thumb in Directory.EnumerateFiles(ThumbnailDirectory, "*.img"))
                if (!used.Contains(Path.GetFileName(thumb)))
                    File.Delete(thumb);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the cache is only an accelerator
        }
    }
}
