using System.Text;
using Sims4ModManager.Core.Catalog;
using Sims4ModManager.Core.Game;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Tray;

namespace Sims4ModManager.Core.Tests;

public class GameIndexTests : IDisposable
{
    private const uint Casp = 0x034AEECB, Geom = 0x015A1849, Modl = 0x01661233, Objd = 0xC0DB5AE7, Dds = 0x00B2D882, SimData = 0x545AC67A;

    private readonly string _root;
    private readonly string _game;
    private readonly string _mods;

    public GameIndexTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "S4MM_Game_" + Guid.NewGuid());
        _game = Path.Combine(_root, "The Sims 4");
        _mods = Path.Combine(_root, "Mods");
        Directory.CreateDirectory(_mods);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static void Write(string path, params TestDbpfBuilder.Entry[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, TestDbpfBuilder.Build(entries));
    }

    private static TestDbpfBuilder.Entry E(uint type, ulong instance, byte[]? data = null, uint group = 0) =>
        new(type, group, instance, data ?? new byte[16]);

    private void WriteGame()
    {
        Write(Path.Combine(_game, "Data", "Client", "ClientFullBuild0.package"), E(Casp, 0x1111_0000_0000_0001), E(Geom, 0x1111_0000_0000_0002), E(Dds, 0x1111_0000_0000_0003));
        Write(Path.Combine(_game, "EP01", "ClientFullBuild0.package"), E(Casp, 0x2222_0000_0000_0001), E(Casp, 0x1111_0000_0000_0001));
        Write(Path.Combine(_game, "Delta", "EP02", "ClientDeltaBuild0.package"), E(Objd, 0x3333_0000_0000_0001));
        Write(Path.Combine(_game, "EP01", "Strings_ENG_US.package"), E(0x220557DA, 0x4444_0000_0000_0001));
        Write(Path.Combine(_game, "NoDVD", "Other.package"), E(Casp, 0x5555_0000_0000_0001));
    }

    [Fact]
    public void GameIndexReadsBaseGamePacksAndPatchesAndPrefersTheBaseGame()
    {
        WriteGame();
        var index = GameIndex.Build(_game);

        Assert.Equal(3, index.PackageCount); // Strings_* and unknown folders are skipped
        Assert.Equal(new[] { "BG", "EP01", "EP02" }, index.Packs.Select(p => p.Code));
        Assert.True(index.Contains(new ResourceKey(Dds, 0, 0x1111_0000_0000_0003)));
        Assert.False(index.Contains(new ResourceKey(Dds, 1, 0x1111_0000_0000_0003)));
        Assert.False(index.Contains(new ResourceKey(0x220557DA, 0, 0x4444_0000_0000_0001)));
        Assert.False(index.ContainsCatalogItem(0x5555_0000_0000_0001));

        Assert.True(index.PackOfCatalogItem(0x1111_0000_0000_0001)!.IsBaseGame); // also in EP01, the base game wins
        Assert.Equal("EP01", index.PackOfCatalogItem(0x2222_0000_0000_0001)!.Code);
        Assert.Equal("EP02", index.PackOfCatalogItem(0x3333_0000_0000_0001)!.Code);
        Assert.Null(index.PackOfCatalogItem(0x1111_0000_0000_0003)); // textures are no catalog items
        Assert.Equal("EP01 Get to Work", index.Packs[1].DisplayName);
    }

    [Fact]
    public void GameIndexCacheIsReusedUntilTheGameChanges()
    {
        WriteGame();
        string cache = Path.Combine(_root, "cache", "gameindex.bin");
        var first = GameIndex.LoadOrBuild(_game, cache);
        Assert.True(File.Exists(cache));

        var loaded = GameIndex.TryLoad(cache)!;
        Assert.Equal(first.KeyCount, loaded.KeyCount);
        Assert.Equal(first.CatalogItemCount, loaded.CatalogItemCount);
        Assert.True(loaded.Contains(new ResourceKey(Geom, 0, 0x1111_0000_0000_0002)));
        Assert.Equal("EP01", loaded.PackOfCatalogItem(0x2222_0000_0000_0001)!.Code);

        // A new pack invalidates the cache.
        Write(Path.Combine(_game, "SP01", "ClientFullBuild0.package"), E(Casp, 0x6666_0000_0000_0001));
        var rebuilt = GameIndex.LoadOrBuild(_game, cache);
        Assert.Equal("SP01", rebuilt.PackOfCatalogItem(0x6666_0000_0000_0001)!.Code);

        File.WriteAllBytes(cache, new byte[] { 1, 2, 3 });
        Assert.Null(GameIndex.TryLoad(cache)); // damaged cache: rebuilt instead of crashing
    }

    [Fact]
    public void ReplacementsAreFoundByKeyAndEaTuningByIdAndName()
    {
        var game = GameIndex.FromKeys(new[]
        {
            (new ResourceKey(Casp, 0, 0x1111_0000_0000_0001), "BG"),
            (new ResourceKey(Dds, 0, 0x1111_0000_0000_0003), "BG"),
            (new ResourceKey(SimData, 0x17, 0x20513), "BG"),
        });
        const uint loot = 0x0C772E27;
        byte[] eaTuning = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>\n<I c=\"LootActions\" i=\"action\" m=\"interactions.utils.loot\" n=\"loot_Aspiration_Unlock\" s=\"132371\"></I>");
        byte[] ownTuning = Encoding.UTF8.GetBytes("<I c=\"LootActions\" i=\"action\" m=\"x\" n=\"Creator_MyLoot\" s=\"12345\"></I>");
        Write(Path.Combine(_mods, "default skin.package"), E(Casp, 0x1111_0000_0000_0001), E(Dds, 0x1111_0000_0000_0003), E(0x3C1AF1F2, 0x1111_0000_0000_0001));
        Write(Path.Combine(_mods, "aspirations.package"), E(loot, 0x20513, eaTuning), E(SimData, 0x20513, group: 0x17));
        Write(Path.Combine(_mods, "own.package"), E(loot, 12345, ownTuning), E(Casp, 0x9999_0000_0000_0001));

        var mods = ModScanner.Scan(_mods);
        var result = GameContentAnalyzer.FindReplacements(mods, game);

        Assert.Equal(2, result.Count);
        var skin = result.Single(r => r.Mod.DisplayName.StartsWith("default"));
        Assert.Equal(1, skin.Counts[ReplacementKind.CasPart]);
        Assert.Equal(1, skin.Counts[ReplacementKind.Texture]);
        Assert.False(skin.TouchesTuning); // the thumbnail is not counted
        var tuning = result.Single(r => r.Mod.DisplayName.StartsWith("aspirations"));
        Assert.Equal(2, tuning.Counts[ReplacementKind.Tuning]); // SimData by key + XML by heuristic
        Assert.Equal(new[] { "loot_Aspiration_Unlock" }, tuning.TuningNames);
    }

    /// <summary>A CAS part whose key list (at the offset in its second uint) names the given GEOM keys.</summary>
    private static byte[] CasPartWithMeshes(params ResourceKey[] meshes)
    {
        var body = CatalogTests.CasPart("Creator_yfTop_Recolor", 6, CasPartInfo.AgeAdult | CasPartInfo.GenderFemale);
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(body);
        long listPos = stream.Position;
        w.Write((byte)(meshes.Length + 1));
        w.Write(0UL); w.Write(0u); w.Write(0u); // first entry is empty in real files
        foreach (var key in meshes)
        {
            w.Write(key.Instance);
            w.Write(key.Group);
            w.Write(key.Type);
        }
        var bytes = stream.ToArray();
        BitConverter.GetBytes((uint)(listPos - 8)).CopyTo(bytes, 4);
        return bytes;
    }

    /// <summary>An OBJD with a model property (instance stored high half first) and a name property.</summary>
    private static byte[] ObjectDefinition(ResourceKey model, string name)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write((ushort)2);
        w.Write(0u); // table offset, patched
        long nameOffset = stream.Position;
        w.Write(name.Length);
        w.Write(Encoding.ASCII.GetBytes(name));
        long modelOffset = stream.Position;
        w.Write(4u);
        w.Write((uint)(model.Instance >> 32));
        w.Write((uint)model.Instance);
        w.Write(model.Type);
        w.Write(model.Group);
        long table = stream.Position;
        w.Write((ushort)2);
        w.Write(0xE7F07786u); w.Write((uint)nameOffset);
        w.Write(0x8D20ACC6u); w.Write((uint)modelOffset);
        var bytes = stream.ToArray();
        BitConverter.GetBytes((uint)table).CopyTo(bytes, 2);
        return bytes;
    }

    [Fact]
    public void MeshReferencesAreReadFromCasPartsAndObjects()
    {
        var geom = new ResourceKey(Geom, 0x8000_1234, 0xABCD_0000_0000_0001);
        Assert.Equal(new[] { geom }, MeshReferenceReader.ReadCasMeshes(CasPartWithMeshes(geom)));

        var model = new ResourceKey(Modl, 0, 0x40A0_8991_63F8_91E0);
        var (read, name) = MeshReferenceReader.ReadObject(ObjectDefinition(model, "Creator_chair"));
        Assert.Equal(model, read);
        Assert.Equal("Creator_chair", name);
    }

    [Fact]
    public void RecolorsWithoutMeshAreFoundUnlessAModOrTheGameHasIt()
    {
        var inGame = new ResourceKey(Geom, 0, 0x1111_0000_0000_0010);
        var inDisabledMod = new ResourceKey(Geom, 0, 0x2222_0000_0000_0010);
        var nowhere = new ResourceKey(Geom, 0, 0x3333_0000_0000_0010);
        var objectModel = new ResourceKey(Modl, 0, 0x4444_0000_0000_0010);
        var game = GameIndex.FromKeys(new[] { (inGame, "BG") });

        Write(Path.Combine(_mods, "recolor ea.package"), E(Casp, 0xA1, CasPartWithMeshes(inGame)));
        Write(Path.Combine(_mods, "recolor disabled mesh.package"), E(Casp, 0xA2, CasPartWithMeshes(inDisabledMod)));
        Write(Path.Combine(_mods, "mesh.package.disabled"), E(Geom, inDisabledMod.Instance));
        Write(Path.Combine(_mods, "recolor lost.package"), E(Casp, 0xA3, CasPartWithMeshes(nowhere)));
        Write(Path.Combine(_mods, "full item.package"), E(Casp, 0xA4, CasPartWithMeshes(new ResourceKey(Geom, 0, 0xA4))), E(Geom, 0xA4));
        Write(Path.Combine(_mods, "table recolor.package"), E(Objd, 0xA5, ObjectDefinition(objectModel, "Creator_table")));

        var mods = ModScanner.Scan(_mods);
        var catalog = new CatalogScanner(Path.Combine(_root, "cache")).Scan(mods);
        var missing = GameContentAnalyzer.FindMissingMeshes(mods, f => catalog.GetValueOrDefault(f), game);

        Assert.Equal(3, missing.Count);
        var disabled = missing.Single(m => m.Status == MeshStatus.Disabled);
        Assert.Equal(inDisabledMod, disabled.Mesh);
        Assert.StartsWith("mesh", disabled.DisabledProvider!.DisplayName);
        Assert.Contains(missing, m => m.Mesh == nowhere && m.Item == "Creator_yfTop_Recolor");
        Assert.Contains(missing, m => m.Mesh == objectModel && m.Item == "Creator_table");
    }

    [Fact]
    public void HouseholdsGetRequiredPacksAndMissingContentFromLearnedFieldPaths()
    {
        var game = GameIndex.FromKeys(new[]
        {
            (new ResourceKey(Casp, 0, 0x1111_0000_0000_0001), "BG"),
            (new ResourceKey(Casp, 0, 0x1111_0000_0000_0002), "BG"),
            (new ResourceKey(Casp, 0, 0x1111_0000_0000_0003), "BG"),
            (new ResourceKey(Casp, 0, 0x2222_0000_0000_0001), "EP01"),
            (new ResourceKey(Casp, 0, 0xF3AE), "GP04"), // old EA ids are small numbers
        });
        Write(Path.Combine(_mods, "cc hair.package"), E(Casp, 0x7777_0000_0000_0001));
        var mods = ModScanner.Scan(_mods);

        // household › sim › outfit › part ids, plus a sim id (not a reference) elsewhere
        var outfit = new TrayTestFiles.Proto()
            .Varint(1, 0x1111_0000_0000_0001).Varint(1, 0x1111_0000_0000_0002).Varint(1, 0x1111_0000_0000_0003)
            .Varint(1, 0x2222_0000_0000_0001).Varint(1, 0x7777_0000_0000_0001).Varint(1, 0x9999_0000_0000_0009)
            .Varint(1, 0xF3AE);
        // 0xF3AE also appears as an age elsewhere: only the learned reference path counts.
        var sim = new TrayTestFiles.Proto().Varint(2, 0x5A5A_5A5A_5A5A_5A5A).Varint(3, 0xF3AE).Message(6, outfit);
        byte[] message = new TrayTestFiles.Proto().Message(1, sim).ToArray();
        var file = new byte[16 + message.Length];
        BitConverter.GetBytes(2).CopyTo(file, 0);
        BitConverter.GetBytes(message.Length).CopyTo(file, 12);
        message.CopyTo(file, 16);

        string tray = Path.Combine(_root, "Tray");
        Directory.CreateDirectory(tray);
        string dataPath = Path.Combine(tray, TrayTestFiles.FileName(0, 0x42, "householdbinary"));
        File.WriteAllBytes(dataPath, file);
        var item = new TrayItem { Id = 0x42, Type = TrayItemType.Household, Files = new[] { dataPath }, Problems = Array.Empty<string>() };

        var check = new TrayContentChecker(game, mods).CheckAll(new[] { item })[item];

        Assert.Equal(new[] { "EP01", "GP04" }, check.RequiredPacks.Select(p => p.Code));
        Assert.True(check.PacksComplete);
        Assert.True(check.MissingCheckSupported);
        Assert.Equal(1, check.MissingCount); // 0x9999… at the outfit path; the sim id path is no reference path
    }
}
