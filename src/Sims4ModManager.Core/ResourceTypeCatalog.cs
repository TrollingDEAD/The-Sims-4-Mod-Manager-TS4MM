using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core;

/// <summary>
/// Human-readable names and conflict severity for well-known Sims 4 resource types.
/// Severity reflects the typical impact when one mod silently overrides another:
/// High - game logic / catalog data (broken interactions, missing objects, errors),
/// Medium - assets such as meshes, textures, strings, animations (visual glitches),
/// Low - thumbnails and name maps (cosmetic only).
/// Unknown types default to Medium.
/// </summary>
public static class ResourceTypeCatalog
{
    public readonly record struct TypeInfo(string Name, ConflictSeverity Severity);

    private static readonly Dictionary<uint, TypeInfo> Known = new()
    {
        // Game logic / tuning
        [0x0333406C] = new("XML-Tuning", ConflictSeverity.High),
        [0x03B33DDF] = new(L.T("Tuning"), ConflictSeverity.High),
        [0x62E94D38] = new(L.T("Combined Tuning"), ConflictSeverity.High),
        [0x545AC67A] = new("SimData", ConflictSeverity.High),
        [0x6017E896] = new(L.T("Buff-Tuning"), ConflictSeverity.High),
        [0xCB5FDDC7] = new(L.T("Trait-Tuning"), ConflictSeverity.High),
        [0xE882D22F] = new(L.T("Interaktions-Tuning"), ConflictSeverity.High),
        [0x0C772E27] = new(L.T("Loot-Tuning"), ConflictSeverity.High),
        [0x7DF2169C] = new(L.T("Snippet-Tuning"), ConflictSeverity.High),
        [0xB61DE6B4] = new(L.T("Objekt-Tuning"), ConflictSeverity.High),

        // Catalog / definitions
        [0x034AEECB] = new(L.T("CAS-Teil"), ConflictSeverity.High),
        [0x319E4F1D] = new(L.T("Objektkatalog"), ConflictSeverity.High),
        [0xC0DB5AE7] = new(L.T("Objektdefinition"), ConflictSeverity.High),
        [0x0354796A] = new(L.T("Hautton"), ConflictSeverity.High),

        // Assets
        [0x015A1849] = new(L.T("Geometrie"), ConflictSeverity.Medium),
        [0x01661233] = new(L.T("Modell"), ConflictSeverity.Medium),
        [0x01D10F34] = new(L.T("Modell-LOD"), ConflictSeverity.Medium),
        [0x01D0E75D] = new(L.T("Materialdefinition"), ConflictSeverity.Medium),
        [0x00B2D882] = new(L.T("Textur (DDS)"), ConflictSeverity.Medium),
        [0x3453CF95] = new(L.T("Textur (RLE2)"), ConflictSeverity.Medium),
        [0xBA856C78] = new(L.T("Textur (RLES)"), ConflictSeverity.Medium),
        [0xAC16FBEC] = new(L.T("Region Map"), ConflictSeverity.Medium),
        [0x8EAF13DE] = new(L.T("Rig"), ConflictSeverity.Medium),
        [0xD3044521] = new(L.T("Slot"), ConflictSeverity.Medium),
        [0xD382BF57] = new(L.T("Footprint"), ConflictSeverity.Medium),
        [0x02D5DF13] = new(L.T("Animations-Statemachine"), ConflictSeverity.Medium),
        [0x6B20C4F3] = new(L.T("Animationsclip"), ConflictSeverity.Medium),
        [0x220557DA] = new(L.T("Stringtabelle"), ConflictSeverity.Medium),
        [0xD5F0F921] = new(L.T("Wandmuster"), ConflictSeverity.Medium),
        [0xB4F762C9] = new(L.T("Bodenmuster"), ConflictSeverity.Medium),
        [0x03B4C61D] = new(L.T("Licht"), ConflictSeverity.Medium),
        [0x8B18FF6E] = new(L.T("Slider-Bereich"), ConflictSeverity.Medium),

        // Cosmetic only
        [0x3C1AF1F2] = new(L.T("CAS-Vorschaubild"), ConflictSeverity.Low),
        [0x3C2A8647] = new(L.T("Kaufmodus-Vorschaubild"), ConflictSeverity.Low),
        [0x5B282D45] = new(L.T("Vorschaubild"), ConflictSeverity.Low),
        [0x0166038C] = new(L.T("Name Map"), ConflictSeverity.Low),
        [0x7FB6AD8A] = new(L.T("Liste der Originaldateien (Merge)"), ConflictSeverity.Low),
    };

    /// <summary>
    /// Resource types that are not game content and therefore cannot conflict - e.g. the manifest
    /// Sims 4 Studio writes (with the same key) into every merged package.
    /// </summary>
    private static readonly HashSet<uint> NotGameContent = new()
    {
        0x7FB6AD8A // Sims 4 Studio merged-package manifest
    };

    public static bool IsIgnoredForConflicts(uint type) => NotGameContent.Contains(type);

    public static bool IsKnown(uint type) => Known.ContainsKey(type);

    public static TypeInfo Describe(uint type) =>
        Known.TryGetValue(type, out var info)
            ? info
            : new TypeInfo(L.F("Typ {0:X8}", type), ConflictSeverity.Medium);
}
