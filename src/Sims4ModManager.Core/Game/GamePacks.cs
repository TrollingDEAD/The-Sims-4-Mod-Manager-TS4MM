using System.Text.RegularExpressions;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Game;

/// <summary>An installed part of the game: the base game or an expansion/game/stuff/kit pack.</summary>
public sealed record GamePack(string Code)
{
    public const string BaseGameCode = "BG";

    public bool IsBaseGame => Code == BaseGameCode;

    /// <summary>"EP01 Get to Work"; packs without a known name (newer than this program) show their code only.</summary>
    public string DisplayName => IsBaseGame ? L.T("Basisspiel") : GamePacks.NameOf(Code) is { } name ? $"{Code} {name}" : Code;

    public override string ToString() => DisplayName;
}

/// <summary>
/// Pack codes and names. The game folders carry no display names (only EP01, GP04, SP13 …), so the
/// names are listed here - in English, as the community uses them across languages.
/// </summary>
public static partial class GamePacks
{
    [GeneratedRegex(@"^(EP|GP|SP|FP)\d{2}$", RegexOptions.IgnoreCase)]
    private static partial Regex PackCode();

    public static bool IsPackCode(string folderName) => PackCode().IsMatch(folderName);

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EP01"] = "Get to Work", ["EP02"] = "Get Together", ["EP03"] = "City Living", ["EP04"] = "Cats & Dogs",
        ["EP05"] = "Seasons", ["EP06"] = "Get Famous", ["EP07"] = "Island Living", ["EP08"] = "Discover University",
        ["EP09"] = "Eco Lifestyle", ["EP10"] = "Snowy Escape", ["EP11"] = "Cottage Living", ["EP12"] = "High School Years",
        ["EP13"] = "Growing Together", ["EP14"] = "Horse Ranch", ["EP15"] = "For Rent", ["EP16"] = "Lovestruck",
        ["EP17"] = "Life & Death", ["EP18"] = "Businesses & Hobbies",
        ["GP01"] = "Outdoor Retreat", ["GP02"] = "Spa Day", ["GP03"] = "Dine Out", ["GP04"] = "Vampires",
        ["GP05"] = "Parenthood", ["GP06"] = "Jungle Adventure", ["GP07"] = "StrangerVille", ["GP08"] = "Realm of Magic",
        ["GP09"] = "Journey to Batuu", ["GP10"] = "Dream Home Decorator", ["GP11"] = "My Wedding Stories", ["GP12"] = "Werewolves",
        ["SP01"] = "Luxury Party Stuff", ["SP02"] = "Perfect Patio Stuff", ["SP03"] = "Cool Kitchen Stuff", ["SP04"] = "Spooky Stuff",
        ["SP05"] = "Movie Hangout Stuff", ["SP06"] = "Romantic Garden Stuff", ["SP07"] = "Kids Room Stuff", ["SP08"] = "Backyard Stuff",
        ["SP09"] = "Vintage Glamour Stuff", ["SP10"] = "Bowling Night Stuff", ["SP11"] = "Fitness Stuff", ["SP12"] = "Toddler Stuff",
        ["SP13"] = "Laundry Day Stuff", ["SP14"] = "My First Pet Stuff", ["SP15"] = "Moschino Stuff", ["SP16"] = "Tiny Living Stuff",
        ["SP17"] = "Nifty Knitting", ["SP18"] = "Paranormal Stuff", ["SP20"] = "Throwback Fit Kit", ["SP21"] = "Country Kitchen Kit",
        ["SP22"] = "Bust the Dust Kit", ["SP23"] = "Courtyard Oasis Kit", ["SP24"] = "Fashion Street Kit", ["SP25"] = "Industrial Loft Kit",
        ["SP26"] = "Incheon Arrivals Kit", ["SP28"] = "Modern Menswear Kit", ["SP29"] = "Blooming Rooms Kit",
        ["SP30"] = "Carnaval Streetwear Kit", ["SP31"] = "Décor to the Max Kit", ["SP32"] = "Moonlight Chic Kit",
        ["SP33"] = "Little Campers Kit", ["SP34"] = "First Fits Kit", ["SP35"] = "Desert Luxe Kit", ["SP36"] = "Pastel Pop Kit",
        ["SP37"] = "Everyday Clutter Kit", ["SP38"] = "Simtimates Collection Kit", ["SP39"] = "Bathroom Clutter Kit",
        ["SP40"] = "Greenhouse Haven Kit", ["SP41"] = "Basement Treasures Kit", ["SP42"] = "Grunge Revival Kit",
        ["SP43"] = "Book Nook Kit", ["SP44"] = "Poolside Splash Kit", ["SP45"] = "Modern Luxe Kit", ["SP46"] = "Home Chef Hustle Stuff",
        ["SP47"] = "Castle Estate Kit", ["SP48"] = "Goth Galore Kit", ["SP49"] = "Crystal Creations Stuff",
        ["FP01"] = "Holiday Celebration Pack",
    };

    public static string? NameOf(string code) => Names.GetValueOrDefault(code);

    /// <summary>Sort order: base game, expansions, game packs, stuff packs/kits, free packs - each by number.</summary>
    public static int SortKey(string code) =>
        code.Length < 4 ? 0 : ("EGSF".IndexOf(char.ToUpperInvariant(code[0])) + 1) * 1000 + (int.TryParse(code[2..], out int n) ? n : 999);
}
