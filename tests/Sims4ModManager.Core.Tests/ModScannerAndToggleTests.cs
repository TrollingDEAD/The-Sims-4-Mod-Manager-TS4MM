namespace Sims4ModManager.Core.Tests;

public class ModScannerAndToggleTests : IDisposable
{
    private readonly string _modsRoot;

    public ModScannerAndToggleTests()
    {
        _modsRoot = Path.Combine(Path.GetTempPath(), "S4MM_Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_modsRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_modsRoot))
            Directory.Delete(_modsRoot, recursive: true);
    }

    [Fact]
    public void ScansLooseFileAndFolderModsSeparately()
    {
        File.WriteAllBytes(Path.Combine(_modsRoot, "LooseMod.package"), new byte[] { 0 });

        string folder = Path.Combine(_modsRoot, "CCPack");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "part1.package"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(folder, "part2.package"), new byte[] { 0 });

        // Unrelated file must not be picked up as a mod.
        File.WriteAllText(Path.Combine(_modsRoot, "notes.txt"), "hello");

        var mods = ModScanner.Scan(_modsRoot);

        Assert.Equal(2, mods.Count);

        var loose = Assert.Single(mods, m => m.DisplayName == "LooseMod");
        Assert.False(loose.IsFolder);
        Assert.Single(loose.Files);

        var pack = Assert.Single(mods, m => m.DisplayName == "CCPack");
        Assert.True(pack.IsFolder);
        Assert.Equal(2, pack.Files.Count);
    }

    [Fact]
    public void ToggleDisableThenEnableRoundTripsFileNames()
    {
        string filePath = Path.Combine(_modsRoot, "MyMod.package");
        File.WriteAllBytes(filePath, new byte[] { 0 });

        var mod = ModScanner.Scan(_modsRoot).Single();
        Assert.True(mod.IsEnabled);

        var disableResult = ModToggleService.SetEnabled(mod, enable: false);
        Assert.True(disableResult.Success);
        Assert.False(File.Exists(filePath));
        Assert.True(File.Exists(filePath + ".disabled"));

        var afterDisable = ModScanner.Scan(_modsRoot).Single();
        Assert.False(afterDisable.IsEnabled);

        var enableResult = ModToggleService.SetEnabled(afterDisable, enable: true);
        Assert.True(enableResult.Success);
        Assert.True(File.Exists(filePath));

        var afterEnable = ModScanner.Scan(_modsRoot).Single();
        Assert.True(afterEnable.IsEnabled);
    }

    [Fact]
    public void FolderModIsPartiallyEnabledWhenOnlySomeFilesAreDisabled()
    {
        string folder = Path.Combine(_modsRoot, "CCPack");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "part1.package"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(folder, "part2.package.disabled"), new byte[] { 0 });

        var mod = ModScanner.Scan(_modsRoot).Single();

        Assert.False(mod.IsEnabled);
        Assert.True(mod.IsPartiallyEnabled);
    }
}
