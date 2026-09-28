using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Catalog;

/// <summary>What a mod file mainly contains, derived from its resources.</summary>
public enum ContentCategory
{
    Other,
    Cas,
    Objects,
    Build,
    Gameplay,
    Script,
    Sliders,
    Poses,
    /// <summary>Only textures/meshes and no catalog entry of its own - almost always a default replacement.</summary>
    AssetsOnly
}

/// <summary>Create-a-Sim area of a CAS file (from the body type of its CAS parts).</summary>
public enum CasCategory
{
    None,
    Hair,
    Clothing,
    Shoes,
    Accessories,
    Makeup,
    EyesBrows,
    Skin,
    Tattoos,
    Nails,
    SkinTone
}

public static class ContentCategories
{
    /// <summary>Maps a CASP body type (measured on real CC, see <see cref="CasPartReader"/>) to its area.</summary>
    public static CasCategory FromBodyType(int bodyType) => bodyType switch
    {
        2 or 28 => CasCategory.Hair,                  // hair, facial hair
        5 or 6 or 7 or 36 or 42 => CasCategory.Clothing, // full body, top, bottom, socks, tights
        8 => CasCategory.Shoes,
        1 or (>= 9 and <= 27) => CasCategory.Accessories, // hats, earrings, glasses, necklaces, gloves, bracelets, piercings, rings
        29 or 30 or 31 or 32 or 33 or 37 => CasCategory.Makeup, // lipstick, eyeshadow, eyeliner, blush, face paint, lashes
        34 or 35 => CasCategory.EyesBrows,
        >= 44 and <= 57 => CasCategory.Tattoos,
        73 or 74 => CasCategory.Nails,
        _ => CasCategory.Skin                         // head, teeth, skin details, overlays …
    };

    /// <summary>German labels (also used as folder names when sorting).</summary>
    public static string Label(ContentCategory category) => category switch
    {
        ContentCategory.Cas => L.T("CAS"),
        ContentCategory.Objects => L.T("Objekte"),
        ContentCategory.Build => L.T("Bauelemente"),
        ContentCategory.Gameplay => L.T("Gameplay"),
        ContentCategory.Script => L.T("Skript-Mods"),
        ContentCategory.Sliders => L.T("Slider & Presets"),
        ContentCategory.Poses => L.T("Posen & Animationen"),
        ContentCategory.AssetsOnly => L.T("Ersatztexturen"),
        _ => L.T("Sonstiges")
    };

    public static string Label(CasCategory category) => category switch
    {
        CasCategory.Hair => L.T("Haare"),
        CasCategory.Clothing => L.T("Kleidung"),
        CasCategory.Shoes => L.T("Schuhe"),
        CasCategory.Accessories => L.T("Accessoires"),
        CasCategory.Makeup => L.T("Make-up"),
        CasCategory.EyesBrows => L.T("Augen & Brauen"),
        CasCategory.Skin => L.T("Haut & Details"),
        CasCategory.Tattoos => L.T("Tattoos"),
        CasCategory.Nails => L.T("Nägel"),
        CasCategory.SkinTone => L.T("Hauttöne"),
        _ => string.Empty
    };

    public static string Label(ContentCategory category, CasCategory cas) =>
        category == ContentCategory.Cas && cas != CasCategory.None ? L.F("CAS · {0}", Label(cas)) : Label(category);

    /// <summary>Human-readable age list from CASP age flags, e.g. "Teen–Senior".</summary>
    public static string AgeLabel(uint ageGender)
    {
        var ages = new List<string>();
        if ((ageGender & CasPartInfo.AgeInfant) != 0) ages.Add(L.T("Säugling"));
        if ((ageGender & CasPartInfo.AgeToddler) != 0) ages.Add(L.T("Kleinkind"));
        if ((ageGender & CasPartInfo.AgeChild) != 0) ages.Add(L.T("Kind"));
        if ((ageGender & CasPartInfo.AgeTeen) != 0) ages.Add(L.T("Teenager"));
        if ((ageGender & CasPartInfo.AgeYoungAdult) != 0) ages.Add(L.T("Junge Erw."));
        if ((ageGender & CasPartInfo.AgeAdult) != 0) ages.Add(L.T("Erwachsene"));
        if ((ageGender & CasPartInfo.AgeElder) != 0) ages.Add(L.T("Senioren"));
        return ages.Count switch
        {
            0 => string.Empty,
            1 => ages[0],
            _ when (ageGender & 0x78) == 0x78 && ages.Count == 4 => L.T("Teenager bis Senioren"),
            _ => string.Join(", ", ages)
        };
    }

    public static string GenderLabel(uint ageGender) =>
        ((ageGender & CasPartInfo.GenderFemale) != 0, (ageGender & CasPartInfo.GenderMale) != 0) switch
        {
            (true, true) => L.T("Unisex"),
            (true, false) => L.T("Weiblich"),
            (false, true) => L.T("Männlich"),
            _ => string.Empty
        };
}
