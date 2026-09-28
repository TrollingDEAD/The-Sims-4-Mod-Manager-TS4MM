using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Catalog;

/// <summary>Classifies package files by the resource types they contain (no payload needed, except for the CAS area).</summary>
public static class ModClassifier
{
    private const uint SkinTone = 0x0354796A;
    private const uint CasPreset = 0xEAA32ADD;
    private const uint AnimationClip = 0x6B20C4F3;
    private const uint ObjectDefinition = 0xC0DB5AE7;

    internal static readonly HashSet<uint> SliderTypes = new()
    {
        0xC5F6763E, // sim modifier
        0x067CAA11, // blend geometry
        0x0355E0A6, // bone delta
        0xB52F5055, // face/body modifier hotspot (CAS sliders)
        0x0D338A3A  // hotspot control
    };

    internal static readonly HashSet<uint> BuildTypes = new()
    {
        0xD5F0F921, 0xB4F762C9, 0x9A20CD1C, 0x0418FE2A, 0x1C1CF1F7, 0x2FAE983E, 0x1D6DF1CF,
        0xEBCBB16C, 0xB0311D0F, 0xF1EDBD86, 0xA057811C, 0x3F0C529A, 0x91EDBD3E, 0x07936CE0,
        0x84C23219, 0x74050B1F
    };

    /// <summary>Textures, meshes and other assets: alone they only replace existing game content.</summary>
    private static readonly HashSet<uint> AssetTypes = new()
    {
        0x00B2D882, 0x3453CF95, 0xBA856C78, 0x015A1849, 0x01661233, 0x01D10F34, 0x01D0E75D,
        0xAC16FBEC, 0x8EAF13DE, 0xD3044521, 0xD382BF57, 0x02D5DF13, 0x6B20C4F3, 0x1B192049,
        0xAC03A936, 0x0355E0A6, 0x067CAA11, 0xBC4A5044, 0x2BC04EDF, 0xF3A38370, 0xB6C8B6A0
    };

    /// <summary>Resources that exist in nearly every package and say nothing about its content.</summary>
    private static readonly HashSet<uint> NeutralTypes = new()
    {
        0x220557DA, // string table
        0x0166038C, // name map
        0x7FB6AD8A, // S4S merge manifest
        0x3C1AF1F2, 0x3C2A8647, 0x5B282D45, 0xCD9DE247, 0x9C925813 // thumbnails, previews
    };

    /// <summary>Category of one package from its resource types; CAS files additionally need <see cref="CasPartReader"/>.</summary>
    public static ContentCategory Classify(ModFileInfo file)
    {
        if (file.Kind == ModFileKind.Script)
            return ContentCategory.Script;

        var types = file.Resources.Select(r => r.Key.Type).ToHashSet();
        if (types.Contains(CasPartReader.ResourceType) || types.Contains(SkinTone))
            return ContentCategory.Cas;
        if (types.Contains(ObjectDefinition) || types.Contains(CatalogObjectReader.ResourceType))
            return ContentCategory.Objects;
        if (types.Overlaps(BuildTypes))
            return ContentCategory.Build;
        if (types.Contains(CasPreset) || (types.Overlaps(SliderTypes) && !types.Contains(AnimationClip)))
            return ContentCategory.Sliders;

        // Everything that is not an asset, a thumbnail or a string table is (almost always) tuning.
        int tuningTypes = types.Count(t => !AssetTypes.Contains(t) && !NeutralTypes.Contains(t));
        int tuningResources = file.Resources.Count(r => !AssetTypes.Contains(r.Key.Type) && !NeutralTypes.Contains(r.Key.Type));
        if (types.Contains(AnimationClip) && tuningResources <= 3)
            return ContentCategory.Poses;
        if (tuningTypes > 0)
            return ContentCategory.Gameplay;
        if (types.Overlaps(AssetTypes))
            return ContentCategory.AssetsOnly;
        return ContentCategory.Other;
    }

    /// <summary>Category of a whole mod: scripts win (a script mod's packages belong to it), otherwise the most common one.</summary>
    public static ContentCategory Classify(IEnumerable<(ModFileInfo File, ContentCategory Category)> files)
    {
        var list = files.ToList();
        if (list.Count == 0)
            return ContentCategory.Other;
        if (list.Any(f => f.Category == ContentCategory.Script))
            return ContentCategory.Script;
        return list.GroupBy(f => f.Category)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Sum(f => f.File.SizeBytes))
            .First().Key;
    }
}
