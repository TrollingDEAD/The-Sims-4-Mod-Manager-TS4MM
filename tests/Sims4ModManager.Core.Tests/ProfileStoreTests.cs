using Sims4ModManager.Core.Models;
namespace Sims4ModManager.Core.Tests;

public class ProfileStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly ProfileStore _store;

    public ProfileStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "S4MM_Profiles_" + Guid.NewGuid());
        _store = new ProfileStore(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void SaveLoadAndDeleteRoundTrip()
    {
        var profile = new Sims4ModManager.Core.Models.ModProfile
        {
            Name = "Gameplay",
            EnabledModIds = new HashSet<string> { "moda", "modb" }
        };

        _store.Save(profile);
        Assert.Contains("Gameplay", _store.ListProfileNames());

        var loaded = _store.Load("Gameplay");
        Assert.NotNull(loaded);
        Assert.Equal(new HashSet<string> { "moda", "modb" }, loaded!.EnabledModIds);

        _store.Delete("Gameplay");
        Assert.DoesNotContain("Gameplay", _store.ListProfileNames());
    }

    [Fact]
    public void ApplyTogglesModsToMatchProfile()
    {
        string modsRoot = Path.Combine(_dir, "Mods");
        Directory.CreateDirectory(modsRoot);
        File.WriteAllBytes(Path.Combine(modsRoot, "A.package"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(modsRoot, "B.package"), new byte[] { 0 });

        var mods = ModScanner.Scan(modsRoot);
        var profile = new Sims4ModManager.Core.Models.ModProfile
        {
            Name = "OnlyA",
            EnabledModIds = new HashSet<string> { "a.package" }
        };

        var result = ProfileStore.Apply(profile, mods);

        Assert.Empty(result.Failures);
        Assert.Empty(result.MissingModIds);
        var afterMods = ModScanner.Scan(modsRoot);
        Assert.True(afterMods.Single(m => m.DisplayName == "A").IsEnabled);
        Assert.False(afterMods.Single(m => m.DisplayName == "B").IsEnabled);
    }

    [Fact]
    public void ProfileReEnablesDisabledLooseFileMod()
    {
        // Regression: the ID of a disabled loose file used to include ".disabled", so a profile
        // captured while it was enabled could never turn it back on.
        string modsRoot = Path.Combine(_dir, "Mods");
        Directory.CreateDirectory(modsRoot);
        File.WriteAllBytes(Path.Combine(modsRoot, "A.package"), new byte[] { 0 });

        var profile = ProfileStore.CaptureCurrent("All", ModScanner.Scan(modsRoot));
        ModToggleService.SetEnabled(ModScanner.Scan(modsRoot).Single(), enable: false);

        var result = ProfileStore.Apply(profile, ModScanner.Scan(modsRoot));

        Assert.Empty(result.MissingModIds);
        Assert.True(ModScanner.Scan(modsRoot).Single().IsEnabled);
    }

    [Fact]
    public void ApplyReportsModsThatNoLongerExist()
    {
        var profile = new ModProfile { Name = "Old", EnabledModIds = new HashSet<string> { "gone.package" } };

        var result = ProfileStore.Apply(profile, Array.Empty<ModEntry>());

        Assert.Equal(new[] { "gone.package" }, result.MissingModIds);
    }

    [Fact]
    public void KeepsNamesWithInvalidFileNameCharactersAndAvoidsCollisions()
    {
        _store.Save(new ModProfile { Name = "Build/Buy", EnabledModIds = new HashSet<string> { "a" } });
        _store.Save(new ModProfile { Name = "Build_Buy", EnabledModIds = new HashSet<string> { "b" } });

        Assert.Equal(new[] { "Build/Buy", "Build_Buy" }, _store.ListProfileNames());
        Assert.Contains("a", _store.Load("Build/Buy")!.EnabledModIds);
        Assert.Contains("b", _store.Load("Build_Buy")!.EnabledModIds);
    }

    [Fact]
    public void SavingSameNameOverwritesCaseInsensitively()
    {
        _store.Save(new ModProfile { Name = "Gameplay", EnabledModIds = new HashSet<string> { "a" } });
        _store.Save(new ModProfile { Name = "gameplay", EnabledModIds = new HashSet<string> { "b" } });

        var name = Assert.Single(_store.ListProfileNames());
        Assert.Equal("gameplay", name);
        Assert.Equal(new HashSet<string> { "b" }, _store.Load("GAMEPLAY")!.EnabledModIds);
    }

    [Fact]
    public void CorruptProfileFileIsSkippedInsteadOfCrashing()
    {
        _store.Save(new ModProfile { Name = "Good", EnabledModIds = new HashSet<string>() });
        File.WriteAllText(Path.Combine(_dir, "Broken.json"), "{ not json");

        Assert.Equal(new[] { "Good" }, _store.ListProfileNames());
        Assert.Null(_store.Load("Broken"));
    }

    [Fact]
    public void RecoversProfileFromBackupWhenMainFileIsCorrupt()
    {
        _store.Save(new ModProfile { Name = "P", EnabledModIds = new HashSet<string> { "v1" } });
        _store.Save(new ModProfile { Name = "P", EnabledModIds = new HashSet<string> { "v2" } });
        File.WriteAllText(Path.Combine(_dir, "P.json"), "garbage");

        var loaded = _store.Load("P");

        Assert.NotNull(loaded);
        Assert.Contains("v1", loaded!.EnabledModIds); // previous version from P.json.bak
    }

    [Fact]
    public void StoresModsPathWithProfile()
    {
        var profile = ProfileStore.CaptureCurrent("WithPath", Array.Empty<ModEntry>(), @"C:\Mods");
        _store.Save(profile);

        Assert.Equal(@"C:\Mods", _store.Load("WithPath")!.ModsPath);
    }
}
