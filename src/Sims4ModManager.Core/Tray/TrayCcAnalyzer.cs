using System.Collections.Concurrent;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Tray;

/// <summary>
/// Finds the custom content (mod package files) a library item uses. Tray data files reference
/// CAS parts, objects and build items by their 64-bit instance id - as protobuf varints or raw
/// little-endian values depending on the file. Instead of fully decoding every (undocumented)
/// format, each data file is scanned at every byte offset for both encodings and the values are
/// looked up in an index of all instance ids provided by packages in the Mods folder (enabled or not).
/// Only catalog-relevant resource types are indexed, and ids below 2^32 are ignored, which makes
/// accidental matches practically impossible (CC instance ids are 64-bit hashes).
/// </summary>
public sealed class TrayCcAnalyzer
{
    /// <summary>Resource types a tray item can reference, with the category shown to the user.</summary>
    internal static readonly Dictionary<uint, string> ReferenceTypes = new()
    {
        // Create-a-Sim
        [0x034AEECB] = "CAS",          // CAS part (hair, clothing, makeup, ...)
        [0x0354796A] = "CAS",          // skin tone
        [0xEAA32ADD] = "CAS",          // CAS preset
        [0xC5F6763E] = "CAS",          // sim modifier (sliders)
        [0x067CAA11] = "CAS",          // blend geometry (sliders)
        [0x0355E0A6] = "CAS",          // bone delta (sliders)
        // Buy mode
        [0xC0DB5AE7] = L.T("Objekte"),      // object definition
        [0x319E4F1D] = L.T("Objekte"),      // object catalog
        // Build mode
        [0xD5F0F921] = L.T("Bau"),          // wall pattern
        [0xB4F762C9] = L.T("Bau"),          // floor pattern
        [0x9A20CD1C] = L.T("Bau"),          // stairs
        [0x0418FE2A] = L.T("Bau"),          // fence
        [0x1C1CF1F7] = L.T("Bau"),          // railing
        [0x2FAE983E] = L.T("Bau"),          // foundation
        [0x1D6DF1CF] = L.T("Bau"),          // column
        [0xEBCBB16C] = L.T("Bau"),          // terrain paint
        [0xB0311D0F] = L.T("Bau"),          // roof trim
        [0xF1EDBD86] = L.T("Bau"),          // roof pattern
        [0xA057811C] = L.T("Bau"),          // frieze
        [0x3F0C529A] = L.T("Bau"),          // spandrel
    };

    private readonly Dictionary<ulong, List<(ModEntry Mod, ModFileInfo File, uint Type)>> _index = new();

    public TrayCcAnalyzer(IEnumerable<ModEntry> mods)
    {
        foreach (var mod in mods)
        {
            foreach (var file in mod.Files.Where(f => f.Kind == ModFileKind.Package))
            {
                foreach (var resource in file.Resources)
                {
                    if (resource.Key.Instance <= uint.MaxValue || !ReferenceTypes.ContainsKey(resource.Key.Type))
                        continue;
                    if (!_index.TryGetValue(resource.Key.Instance, out var owners))
                        _index[resource.Key.Instance] = owners = new List<(ModEntry, ModFileInfo, uint)>(1);
                    owners.Add((mod, file, resource.Key.Type));
                }
            }
        }
    }

    public int IndexedInstanceCount => _index.Count;

    /// <summary>Mod files referenced by <paramref name="item"/>, enabled first, then by name.</summary>
    public IReadOnlyList<TrayCcReference> Analyze(TrayItem item)
    {
        var instances = new HashSet<ulong>();
        foreach (string path in item.Files)
        {
            string extension = Path.GetExtension(path).TrimStart('.');
            if (!TrayFileName.DataExtensions.Contains(extension))
                continue; // thumbnails carry no references

            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            CollectReferencedInstances(data, instances);
        }

        return ToReferences(instances);
    }

    /// <summary>CC referenced anywhere in the given data blocks (e.g. the decompressed resources of a save).</summary>
    public IReadOnlyList<TrayCcReference> AnalyzeData(IEnumerable<byte[]> blocks)
    {
        var instances = new HashSet<ulong>();
        foreach (var block in blocks)
            CollectReferencedInstances(block, instances);
        return ToReferences(instances);
    }

    private IReadOnlyList<TrayCcReference> ToReferences(HashSet<ulong> instances)
    {
        return instances
            .SelectMany(i => _index[i].Select(o => (o.Mod, o.File, o.Type, Instance: i)))
            .GroupBy(x => x.File)
            .Select(g => new TrayCcReference(
                g.First().Mod,
                g.Key,
                g.Select(x => x.Instance).Distinct().Count(),
                g.Select(x => ReferenceTypes[x.Type]).Distinct().OrderBy(c => c).ToList()))
            .OrderByDescending(r => r.IsEnabled)
            .ThenBy(r => r.Mod.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.File.RelativePathInMod, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Analyzes all items in parallel.</summary>
    public IReadOnlyDictionary<TrayItem, IReadOnlyList<TrayCcReference>> AnalyzeAll(IEnumerable<TrayItem> items)
    {
        var result = new ConcurrentDictionary<TrayItem, IReadOnlyList<TrayCcReference>>();
        Parallel.ForEach(items, item => result[item] = Analyze(item));
        return result;
    }

    private void CollectReferencedInstances(byte[] data, HashSet<ulong> found)
    {
        for (int i = 0; i < data.Length; i++)
        {
            int pos = i;
            if (ProtoReader.TryReadVarint(data, ref pos, data.Length, out ulong varint)
                && varint > uint.MaxValue && _index.ContainsKey(varint))
                found.Add(varint);

            if (i + 8 <= data.Length)
            {
                ulong raw = BitConverter.ToUInt64(data, i);
                if (raw > uint.MaxValue && _index.ContainsKey(raw))
                    found.Add(raw);
            }
        }
    }
}
