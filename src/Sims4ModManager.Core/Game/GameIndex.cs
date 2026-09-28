using System.Collections.Concurrent;
using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Tray;

namespace Sims4ModManager.Core.Game;

/// <summary>
/// Which resources the installed game itself provides - read from the resource indexes of its
/// packages (base game, patches in "Delta", every installed pack), never from the payloads. With it
/// the app can tell mods that replace game content (default replacements, tuning overrides), CC whose
/// mesh is missing, and which packs a library item needs.
/// <para>
/// Keys are kept as 64-bit hashes in a sorted array (about 21 MB for a fully expanded game instead of
/// several hundred MB as objects). String tables are left out: the 18 languages would quadruple the
/// number of packages to read for a rarely needed answer.
/// </para>
/// </summary>
public sealed class GameIndex
{
    private const uint FileMagic = 0x58444947; // "GIDX"
    private const int FormatVersion = 1;

    private readonly ulong[] _keyHashes;          // sorted
    private readonly ulong[] _catalogInstances;   // sorted
    private readonly byte[] _catalogPacks;        // index into Packs, parallel to _catalogInstances

    private GameIndex(string installFolder, string signature, DateTime builtUtc, int packageCount,
        IReadOnlyList<GamePack> packs, ulong[] keyHashes, ulong[] catalogInstances, byte[] catalogPacks)
    {
        InstallFolder = installFolder;
        Signature = signature;
        BuiltUtc = builtUtc;
        PackageCount = packageCount;
        Packs = packs;
        _keyHashes = keyHashes;
        _catalogInstances = catalogInstances;
        _catalogPacks = catalogPacks;
    }

    public string InstallFolder { get; }
    public DateTime BuiltUtc { get; }
    public int PackageCount { get; }

    /// <summary>Installed packs; the first entry is always the base game.</summary>
    public IReadOnlyList<GamePack> Packs { get; }

    public int KeyCount => _keyHashes.Length;
    public int CatalogItemCount => _catalogInstances.Length;
    internal string Signature { get; }

    /// <summary>True if the game provides a resource with exactly this key.</summary>
    public bool Contains(ResourceKey key) => Array.BinarySearch(_keyHashes, Hash(key)) >= 0;

    /// <summary>True if a game catalog item (CAS part, object, build item) has this instance id.</summary>
    public bool ContainsCatalogItem(ulong instance) => Array.BinarySearch(_catalogInstances, instance) >= 0;

    /// <summary>The pack a catalog item comes from (base game if it exists there too); null if unknown.</summary>
    public GamePack? PackOfCatalogItem(ulong instance)
    {
        int i = Array.BinarySearch(_catalogInstances, instance);
        return i >= 0 ? Packs[_catalogPacks[i]] : null;
    }

    /// <summary>The package files that make up the game, with the pack each belongs to.</summary>
    public static IReadOnlyList<(string Path, string PackCode)> FindPackages(string installFolder)
    {
        var result = new List<(string, string)>();
        if (!Directory.Exists(installFolder))
            return result;
        foreach (string dir in Directory.EnumerateDirectories(installFolder))
        {
            string name = Path.GetFileName(dir);
            if (name.Equals("Data", StringComparison.OrdinalIgnoreCase))
                AddFiles(dir, GamePack.BaseGameCode);
            else if (GamePacks.IsPackCode(name))
                AddFiles(dir, name.ToUpperInvariant());
            else if (name.Equals("Delta", StringComparison.OrdinalIgnoreCase))
            {
                // Patches: Delta\EP01\… belongs to EP01, anything else to the base game.
                foreach (string sub in Directory.EnumerateDirectories(dir))
                {
                    string subName = Path.GetFileName(sub);
                    AddFiles(sub, GamePacks.IsPackCode(subName) ? subName.ToUpperInvariant() : GamePack.BaseGameCode);
                }
            }
        }
        return result;

        void AddFiles(string dir, string pack)
        {
            foreach (string file in Directory.EnumerateFiles(dir, "*.package", SearchOption.AllDirectories))
                if (!Path.GetFileName(file).StartsWith("Strings_", StringComparison.OrdinalIgnoreCase))
                    result.Add((file, pack));
        }
    }

    /// <summary>Changes whenever the game is patched or a pack is added or removed.</summary>
    public static string ComputeSignature(IReadOnlyList<(string Path, string PackCode)> packages)
    {
        long size = 0, newest = 0;
        foreach (var (path, _) in packages)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                continue;
            size += info.Length;
            newest = Math.Max(newest, info.LastWriteTimeUtc.Ticks);
        }
        return $"{FormatVersion}:{packages.Count}:{size}:{newest}";
    }

    public static GameIndex Build(string installFolder, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        var packages = FindPackages(installFolder);
        if (packages.Count == 0)
            throw new DirectoryNotFoundException(Localization.L.F("In {0} wurden keine Spieldateien gefunden.", installFolder));

        var packs = packages.Select(p => p.PackCode).Distinct()
            .OrderBy(GamePacks.SortKey).ThenBy(c => c, StringComparer.Ordinal)
            .Select(c => new GamePack(c)).ToList();
        if (!packs[0].IsBaseGame)
            packs.Insert(0, new GamePack(GamePack.BaseGameCode));
        var packIndex = packs.Select((p, i) => (p.Code, i)).ToDictionary(x => x.Code, x => (byte)x.i);

        var keyParts = new ConcurrentBag<ulong[]>();
        var catalog = new ConcurrentDictionary<ulong, byte>();
        int done = 0;
        Parallel.ForEach(packages, new ParallelOptions { CancellationToken = cancel, MaxDegreeOfParallelism = Environment.ProcessorCount },
            package =>
            {
                var resources = DbpfReader.TryReadIndex(package.Path);
                if (resources is not null)
                {
                    var hashes = new ulong[resources.Count];
                    byte pack = packIndex[package.PackCode];
                    for (int i = 0; i < resources.Count; i++)
                    {
                        var key = resources[i].Key;
                        hashes[i] = Hash(key);
                        if (TrayCcAnalyzer.ReferenceTypes.ContainsKey(key.Type))
                            catalog.AddOrUpdate(key.Instance, pack, (_, existing) => Preferred(existing, pack));
                    }
                    keyParts.Add(hashes);
                }
                progress?.Report((double)Interlocked.Increment(ref done) / packages.Count);
            });

        var keys = keyParts.SelectMany(k => k).ToArray();
        Array.Sort(keys);
        keys = Distinct(keys);

        var instances = catalog.Keys.ToArray();
        Array.Sort(instances);
        var instancePacks = instances.Select(i => catalog[i]).ToArray();

        return new GameIndex(installFolder, ComputeSignature(packages), DateTime.UtcNow, packages.Count, packs, keys, instances, instancePacks);
    }

    /// <summary>The base game (index 0) wins, otherwise the lower pack index - content present in several packs is rare.</summary>
    private static byte Preferred(byte a, byte b) => Math.Min(a, b);

    /// <summary>
    /// Loads the cached index if it still matches the installed game, otherwise builds and caches a new one.
    /// </summary>
    public static GameIndex LoadOrBuild(string installFolder, string cachePath, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        string signature = ComputeSignature(FindPackages(installFolder));
        var cached = TryLoad(cachePath);
        if (cached is not null && cached.Signature == signature
            && string.Equals(cached.InstallFolder, installFolder, StringComparison.OrdinalIgnoreCase))
            return cached;

        var index = Build(installFolder, progress, cancel);
        try { index.Save(cachePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* works without cache, just slower next time */ }
        return index;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(FileMagic);
            writer.Write(FormatVersion);
            writer.Write(InstallFolder);
            writer.Write(Signature);
            writer.Write(BuiltUtc.Ticks);
            writer.Write(PackageCount);
            writer.Write(Packs.Count);
            foreach (var pack in Packs)
                writer.Write(pack.Code);
            WriteArray(writer, _keyHashes);
            WriteArray(writer, _catalogInstances);
            writer.Write(_catalogPacks.Length);
            writer.Write(_catalogPacks);
        }
        File.Move(temp, path, overwrite: true);
    }

    public static GameIndex? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != FileMagic || reader.ReadInt32() != FormatVersion)
                return null;
            string folder = reader.ReadString();
            string signature = reader.ReadString();
            var built = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            int packageCount = reader.ReadInt32();
            int packCount = reader.ReadInt32();
            if (packCount is < 1 or > 255)
                return null;
            var packs = Enumerable.Range(0, packCount).Select(_ => new GamePack(reader.ReadString())).ToList();
            var keys = ReadArray(reader);
            var instances = ReadArray(reader);
            int packBytes = reader.ReadInt32();
            if (packBytes != instances.Length)
                return null;
            var instancePacks = reader.ReadBytes(packBytes);
            if (instancePacks.Length != packBytes || instancePacks.Any(p => p >= packCount))
                return null;
            return new GameIndex(folder, signature, built, packageCount, packs, keys, instances, instancePacks);
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or UnauthorizedAccessException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>For tests: an index over explicit keys, as if they came from the given packs.</summary>
    public static GameIndex FromKeys(IEnumerable<(ResourceKey Key, string PackCode)> keys)
    {
        var list = keys.ToList();
        var packs = new[] { GamePack.BaseGameCode }.Concat(list.Select(k => k.PackCode)).Distinct()
            .OrderBy(GamePacks.SortKey).Select(c => new GamePack(c)).ToList();
        var packIndex = packs.Select((p, i) => (p.Code, i)).ToDictionary(x => x.Code, x => (byte)x.i);
        var hashes = list.Select(k => Hash(k.Key)).Distinct().Order().ToArray();
        var catalog = list.Where(k => TrayCcAnalyzer.ReferenceTypes.ContainsKey(k.Key.Type))
            .GroupBy(k => k.Key.Instance)
            .ToDictionary(g => g.Key, g => g.Select(k => packIndex[k.PackCode]).Min());
        var instances = catalog.Keys.Order().ToArray();
        return new GameIndex("(test)", "", DateTime.UtcNow, 0, packs, hashes, instances, instances.Select(i => catalog[i]).ToArray());
    }

    /// <summary>Stable 64-bit mix of the full key (the cache must hash identically across runs).</summary>
    internal static ulong Hash(ResourceKey key) =>
        Mix(key.Instance ^ Mix(((ulong)key.Type << 32) | key.Group));

    private static ulong Mix(ulong x)
    {
        x += 0x9E3779B97F4A7C15;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EB;
        return x ^ (x >> 31);
    }

    private static ulong[] Distinct(ulong[] sorted)
    {
        if (sorted.Length == 0)
            return sorted;
        int count = 1;
        for (int i = 1; i < sorted.Length; i++)
            if (sorted[i] != sorted[count - 1])
                sorted[count++] = sorted[i];
        Array.Resize(ref sorted, count);
        return sorted;
    }

    private static void WriteArray(BinaryWriter writer, ulong[] values)
    {
        writer.Write(values.Length);
        writer.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()));
    }

    private static ulong[] ReadArray(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0 || (long)count * 8 > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new EndOfStreamException();
        var values = new ulong[count];
        reader.BaseStream.ReadExactly(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()));
        return values;
    }
}
