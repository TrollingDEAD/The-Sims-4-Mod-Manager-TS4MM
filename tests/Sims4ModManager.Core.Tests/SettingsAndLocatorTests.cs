namespace Sims4ModManager.Core.Tests;

public class SettingsAndLocatorTests : IDisposable
{
    private readonly string _root;

    public SettingsAndLocatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "S4MM_Settings_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>Creates "&lt;docs&gt;\Electronic Arts\&lt;name&gt;" with game marker files and optionally a Mods folder.</summary>
    private string CreateGameDataFolder(string docs, string name, bool withMods = true, bool withMarkers = true, DateTime? lastPlayed = null)
    {
        string dir = Path.Combine(docs, "Electronic Arts", name);
        Directory.CreateDirectory(dir);
        if (withMarkers)
        {
            File.WriteAllText(Path.Combine(dir, "Options.ini"), "");
            File.WriteAllText(Path.Combine(dir, "GameVersion.txt"), "");
            Directory.CreateDirectory(Path.Combine(dir, "saves"));
            if (lastPlayed is not null)
                File.SetLastWriteTimeUtc(Path.Combine(dir, "Options.ini"), lastPlayed.Value);
        }
        if (withMods)
            Directory.CreateDirectory(Path.Combine(dir, "Mods"));
        return dir;
    }

    [Theory]
    [InlineData("The Sims 4")]
    [InlineData("Die Sims 4")]
    [InlineData("Les Sims 4")]
    public void DetectsLocalizedGameDataFolders(string folderName)
    {
        string docs = Path.Combine(_root, "Docs");
        string dataDir = CreateGameDataFolder(docs, folderName);

        Assert.Equal(Path.Combine(dataDir, "Mods"), ModsFolderLocator.TryAutoDetect(new[] { docs }));
    }

    [Fact]
    public void DetectsUnknownFolderNameByMarkerFiles()
    {
        string docs = Path.Combine(_root, "Docs");
        string dataDir = CreateGameDataFolder(docs, "Os Sims Quatro");

        Assert.Equal(Path.Combine(dataDir, "Mods"), ModsFolderLocator.TryAutoDetect(new[] { docs }));
    }

    [Fact]
    public void IgnoresUnrelatedElectronicArtsFolders()
    {
        string docs = Path.Combine(_root, "Docs");
        Directory.CreateDirectory(Path.Combine(docs, "Electronic Arts", "Battlefield 2042", "Mods"));

        Assert.Null(ModsFolderLocator.TryAutoDetect(new[] { docs }));
    }

    [Fact]
    public void PrefersMostRecentlyPlayedInstallationAcrossDocumentsRoots()
    {
        string localDocs = Path.Combine(_root, "Local");
        string oneDriveDocs = Path.Combine(_root, "OneDrive");
        CreateGameDataFolder(localDocs, "The Sims 4", lastPlayed: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        string recent = CreateGameDataFolder(oneDriveDocs, "Die Sims 4", lastPlayed: DateTime.UtcNow.AddMinutes(5));

        var folders = ModsFolderLocator.FindGameDataFolders(new[] { localDocs, oneDriveDocs });

        Assert.Equal(2, folders.Count);
        Assert.Equal(recent, folders[0].Path);
    }

    [Fact]
    public void PrefersInstallationWithModsFolder()
    {
        string docs = Path.Combine(_root, "Docs");
        CreateGameDataFolder(docs, "The Sims 4", withMods: false, lastPlayed: DateTime.UtcNow.AddMinutes(5));
        string withMods = CreateGameDataFolder(docs, "Die Sims 4");

        Assert.Equal(Path.Combine(withMods, "Mods"), ModsFolderLocator.TryAutoDetect(new[] { docs }));
    }

    [Fact]
    public void ReportsModFilesMisplacedInGameDataFolderButNotGameCaches()
    {
        string docs = Path.Combine(_root, "Docs");
        string dataDir = CreateGameDataFolder(docs, "Die Sims 4");
        File.WriteAllBytes(Path.Combine(dataDir, "CoolHair.package"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(dataDir, "localthumbcache.package"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(dataDir, "clientDB.package"), new byte[] { 0 });

        var folder = ModsFolderLocator.TryGetGameDataFolder(Path.Combine(dataDir, "Mods"));

        Assert.NotNull(folder);
        Assert.Equal("CoolHair.package", Path.GetFileName(Assert.Single(folder!.MisplacedModFiles)));
    }

    [Fact]
    public void NormalizesSelectionOfGameDataFolderToItsModsFolder()
    {
        string dataDir = CreateGameDataFolder(Path.Combine(_root, "Docs"), "Die Sims 4");

        Assert.Equal(Path.Combine(dataDir, "Mods"), ModsFolderLocator.NormalizeSelectedFolder(dataDir + "\\"));
        Assert.Equal(Path.Combine(dataDir, "Mods"), ModsFolderLocator.NormalizeSelectedFolder(Path.Combine(dataDir, "Mods")));

        string unrelated = Path.Combine(_root, "Somewhere");
        Directory.CreateDirectory(unrelated);
        Assert.Equal(unrelated, ModsFolderLocator.NormalizeSelectedFolder(unrelated));
    }

    [Fact]
    public void SettingsRoundTripAndRecentListIsDistinctAndCapped()
    {
        var store = new AppSettingsStore(Path.Combine(_root, "settings.json"), autoDetect: () => null);

        for (int i = 0; i < AppSettingsStore.MaxRecentModsPaths + 3; i++)
            Assert.True(store.TryRememberModsPath($@"C:\Mods{i}"));
        Assert.True(store.TryRememberModsPath(@"c:\mods3"));
        Assert.True(store.TryUpdate(s => s.LastProfileName = "Gameplay"));

        var loaded = store.Load();

        Assert.Equal(@"c:\mods3", loaded.ModsPath);
        Assert.Equal(AppSettingsStore.MaxRecentModsPaths, loaded.RecentModsPaths.Count);
        Assert.Equal(@"c:\mods3", loaded.RecentModsPaths[0]);
        Assert.Single(loaded.RecentModsPaths, p => p.Equals(@"C:\Mods3", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Gameplay", loaded.LastProfileName);
    }

    [Fact]
    public void ReadsSettingsFileWrittenByOlderVersion()
    {
        string path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, """{ "ModsPath": "C:\\Old\\Mods" }""");

        var loaded = new AppSettingsStore(path, autoDetect: () => null).Load();

        Assert.Equal(@"C:\Old\Mods", loaded.ModsPath);
        Assert.Equal(new[] { @"C:\Old\Mods" }, loaded.RecentModsPaths);
        Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [Fact]
    public void CorruptSettingsFallBackToBackupThenDefaults()
    {
        string path = Path.Combine(_root, "settings.json");
        var store = new AppSettingsStore(path, autoDetect: () => null);
        store.TryRememberModsPath(@"C:\First");
        store.TryRememberModsPath(@"C:\Second");

        File.WriteAllText(path, "{ broken");
        Assert.Equal(@"C:\First", store.Load().ModsPath); // from settings.json.bak

        File.WriteAllText(path + ".bak", "also broken");
        Assert.Null(store.Load().ModsPath);
    }

    [Fact]
    public void ResolveReportsMissingSavedFolderAndFallsBackToAutoDetection()
    {
        string detected = Path.Combine(_root, "Detected");
        var store = new AppSettingsStore(Path.Combine(_root, "settings.json"), autoDetect: () => detected);
        store.TryRememberModsPath(Path.Combine(_root, "DoesNotExist"));

        var resolution = store.ResolveModsPath();

        Assert.Equal(detected, resolution.Path);
        Assert.Equal(ModsPathSource.AutoDetected, resolution.Source);
        Assert.Contains("DoesNotExist", resolution.Notice);
    }

    [Fact]
    public void ResolvePrefersExistingSavedFolder()
    {
        string saved = Path.Combine(_root, "Saved");
        Directory.CreateDirectory(saved);
        var store = new AppSettingsStore(Path.Combine(_root, "settings.json"), autoDetect: () => "other");
        store.TryRememberModsPath(saved);

        var resolution = store.ResolveModsPath();

        Assert.Equal(saved, resolution.Path);
        Assert.Equal(ModsPathSource.Saved, resolution.Source);
        Assert.Null(resolution.Notice);
    }
}
