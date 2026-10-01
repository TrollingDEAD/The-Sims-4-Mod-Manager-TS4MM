using Sims4ModManager.Core.Online;

namespace Sims4ModManager.Core.Tests;

public class ModpackFileTests : IDisposable
{
    private readonly string _path;

    public ModpackFileTests()
    {
        _path = Path.Combine(Path.GetTempPath(), "S4MM_Modpack_" + Guid.NewGuid() + ".json");
    }

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Fact]
    public void SaveAndLoadRoundTrips()
    {
        var manifest = new ModpackManifest
        {
            Title = "My Pack",
            Mods = new[] { new ModpackEntry(1, 100, "Mod A"), new ModpackEntry(2, 200, "Mod B") }
        };

        ModpackFile.SaveToFile(manifest, _path);
        var loaded = ModpackFile.TryLoadFromFile(_path);

        Assert.NotNull(loaded);
        Assert.Equal("My Pack", loaded!.Title);
        Assert.Equal(2, loaded.Mods.Count);
        Assert.Contains(loaded.Mods, m => m.ModId == 1 && m.FileId == 100 && m.DisplayName == "Mod A");
    }

    [Fact]
    public void LoadReturnsNullForMissingFile() => Assert.Null(ModpackFile.TryLoadFromFile(_path));

    [Fact]
    public void LoadReturnsNullForEmptyModList()
    {
        var manifest = new ModpackManifest { Mods = Array.Empty<ModpackEntry>() };
        ModpackFile.SaveToFile(manifest, _path);

        Assert.Null(ModpackFile.TryLoadFromFile(_path));
    }

    [Fact]
    public void LoadReturnsNullForANewerFormatVersion()
    {
        var manifest = new ModpackManifest
        {
            FormatVersion = ModpackManifest.CurrentFormatVersion + 1,
            Mods = new[] { new ModpackEntry(1, 100, "Mod A") }
        };
        ModpackFile.SaveToFile(manifest, _path);

        Assert.Null(ModpackFile.TryLoadFromFile(_path));
    }

    [Fact]
    public void LoadReturnsNullForGarbageContent()
    {
        File.WriteAllText(_path, "not json at all");
        Assert.Null(ModpackFile.TryLoadFromFile(_path));
    }
}
