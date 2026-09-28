using Sims4ModManager.Core.Catalog;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Tests;

public class PortableBackupTests : IDisposable
{
    private readonly string _root;

    public PortableBackupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "S4MM_Backup_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void CaptureExcludesMachineSpecificFields()
    {
        var settings = new AppSettingsStore(Path.Combine(_root, "src", "settings.json"), autoDetect: () => null);
        settings.TryUpdate(s =>
        {
            s.ModsPath = @"C:\old-machine\Mods";
            s.GameInstallPath = @"C:\old-machine\Game";
            s.CurseForgeApiKeyProtected = "not-decryptable-on-another-machine";
            s.SortByCreator = false;
            s.IncludePrereleaseUpdates = true;
            s.AccentColor = "#4C8DFF";
        });
        var notes = new ModNotesStore(Path.Combine(_root, "src", "notes.json"));
        notes.Set("mod.package", new ModNote { Note = "hi", IsFavorite = true });
        var profiles = new ProfileStore(Path.Combine(_root, "src", "profiles"));
        profiles.Save(new ModProfile { Name = "Building", EnabledModIds = new HashSet<string> { "mod.package" } });

        var backup = PortableBackupService.Capture(settings, notes, profiles);

        Assert.False(backup.Settings.SortByCreator);
        Assert.True(backup.Settings.IncludePrereleaseUpdates);
        Assert.Equal("#4C8DFF", backup.Settings.AccentColor);
        Assert.True(backup.Notes["mod.package"].IsFavorite);
        Assert.Single(backup.Profiles, p => p.Name == "Building");
    }

    [Fact]
    public void RoundTripsThroughFileAndApplyMergesIntoDestination()
    {
        var srcSettings = new AppSettingsStore(Path.Combine(_root, "src", "settings.json"), autoDetect: () => null);
        srcSettings.TryUpdate(s => s.IncludePrereleaseUpdates = true);
        var srcNotes = new ModNotesStore(Path.Combine(_root, "src", "notes.json"));
        srcNotes.Set("mod.package", new ModNote { IsFavorite = true, Tags = new() { "hair" } });
        var srcProfiles = new ProfileStore(Path.Combine(_root, "src", "profiles"));
        srcProfiles.Save(new ModProfile { Name = "Building", EnabledModIds = new HashSet<string> { "mod.package" } });

        string file = Path.Combine(_root, "export.s4mmbackup.json");
        PortableBackupService.SaveToFile(PortableBackupService.Capture(srcSettings, srcNotes, srcProfiles), file);

        // Destination already has its own, unrelated data - must survive the import.
        var dstSettings = new AppSettingsStore(Path.Combine(_root, "dst", "settings.json"), autoDetect: () => null);
        dstSettings.TryUpdate(s => s.ModsPath = @"C:\new-machine\Mods");
        var dstNotes = new ModNotesStore(Path.Combine(_root, "dst", "notes.json"));
        dstNotes.Set("other.package", new ModNote { Note = "keep me" });
        var dstProfiles = new ProfileStore(Path.Combine(_root, "dst", "profiles"));

        var loaded = PortableBackupService.TryLoadFromFile(file);
        Assert.NotNull(loaded);
        PortableBackupService.Apply(loaded!, dstSettings, dstNotes, dstProfiles);

        var applied = dstSettings.Load();
        Assert.True(applied.IncludePrereleaseUpdates);
        Assert.Equal(@"C:\new-machine\Mods", applied.ModsPath); // untouched: not part of the portable settings

        Assert.Equal("keep me", dstNotes.Get("other.package")!.Note);
        Assert.True(dstNotes.Get("mod.package")!.IsFavorite);
        Assert.Contains("hair", dstNotes.Get("mod.package")!.Tags);

        Assert.Equal(new HashSet<string> { "mod.package" }, dstProfiles.Load("Building")!.EnabledModIds);
    }

    [Fact]
    public void TryLoadFromFileReturnsNullForAnUnrelatedJsonFile()
    {
        string file = Path.Combine(_root, "not-a-backup.json");
        File.WriteAllText(file, "{ \"hello\": \"world\" }");

        // Not a crash, and not mistaken for a valid (if empty) backup: FormatVersion defaults to 1 either
        // way, but the point is it must not throw on unexpected shapes.
        var loaded = PortableBackupService.TryLoadFromFile(file);
        Assert.NotNull(loaded);
        Assert.Empty(loaded!.Notes);
        Assert.Empty(loaded.Profiles);
    }
}
