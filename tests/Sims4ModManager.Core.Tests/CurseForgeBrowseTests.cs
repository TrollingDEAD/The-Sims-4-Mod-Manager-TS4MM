using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Catalog;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Online;

namespace Sims4ModManager.Core.Tests;

public class CurseForgeBrowseTests : IDisposable
{
    private readonly string _root;

    public CurseForgeBrowseTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "S4MM_Browse_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task SearchModsAsyncBuildsTheExpectedQueryAndParsesResultsAndPaging()
    {
        var fake = new FakeCurseForge
        {
            Respond = r => r.RequestUri!.AbsolutePath == "/v1/mods/search"
                ? """
                    {"data":[{"id":10,"name":"Test Mod","summary":"A test mod","links":{"websiteUrl":"https://www.curseforge.com/sims4/mods/test-mod"},
                      "authors":[{"name":"Alice"}],"latestFiles":[{"id":501,"modId":10,"displayName":"Test 1.0","fileName":"test.package","releaseType":1,
                      "fileDate":"2025-01-01T00:00:00Z","isAvailable":true}],"allowModDistribution":true,
                      "logo":{"thumbnailUrl":"https://media/thumb.png"},"categories":[{"id":5,"name":"Objects","classId":4,"isClass":false}],"downloadCount":123}],
                     "pagination":{"index":0,"pageSize":30,"resultCount":1,"totalCount":57}}
                    """
                : "{}"
        };
        var client = new CurseForgeClient("secret", fake);

        var (mods, total) = await client.SearchModsAsync("cool stuff", classId: 4, categoryId: 5,
            CurseForgeClient.SortField.Popularity, index: 0, pageSize: 30);

        Assert.Equal(57, total);
        var mod = Assert.Single(mods);
        Assert.Equal("Test Mod", mod.Name);
        Assert.Equal("https://media/thumb.png", mod.Logo?.ThumbnailUrl);
        Assert.Equal("Objects", Assert.Single(mod.Categories).Name);
        Assert.Equal(123, mod.DownloadCount);

        string request = Assert.Single(fake.Requests);
        Assert.Contains("gameId=78062", request);
        Assert.Contains("classId=4", request);
        Assert.Contains("categoryId=5", request);
        Assert.Contains("sortField=2", request); // Popularity
        Assert.Contains("sortOrder=desc", request);
        Assert.Contains("searchFilter=cool%20stuff", request);
    }

    [Fact]
    public async Task GetFilesAsyncBatchesOverFiveHundredIdsAndParsesDependencies()
    {
        var fake = new FakeCurseForge
        {
            Respond = r => r.RequestUri!.AbsolutePath == "/v1/mods/files"
                ? """{"data":[{"id":1,"modId":10,"fileName":"a.package","isAvailable":true,"dependencies":[{"modId":99,"relationType":3}]}]}"""
                : "{}"
        };
        var client = new CurseForgeClient("secret", fake);

        var ids = Enumerable.Range(1, 501).Select(i => (long)i).ToList();
        var files = await client.GetFilesAsync(ids);

        Assert.Equal(2, fake.Requests.Count); // 501 ids batched at 500 -> two POSTs
        Assert.NotEmpty(files);
        var dependency = Assert.Single(files.First().Dependencies);
        Assert.Equal(99, dependency.ModId);
        Assert.Equal(3, dependency.RelationType);
    }

    [Fact]
    public async Task GetCategoriesAsyncPassesClassIdAndClassesOnly()
    {
        var fake = new FakeCurseForge
        {
            Respond = r => r.RequestUri!.AbsolutePath == "/v1/categories"
                ? """{"data":[{"id":4,"name":"Mods","classId":0,"isClass":true,"parentCategoryId":null}]}"""
                : "{}"
        };
        var client = new CurseForgeClient("secret", fake);

        var categories = await client.GetCategoriesAsync(classId: null, classesOnly: true);

        var category = Assert.Single(categories);
        Assert.True(category.IsClass);
        Assert.Null(category.ParentCategoryId);
        string request = Assert.Single(fake.Requests);
        Assert.Contains("gameId=78062", request);
        Assert.Contains("classesOnly=true", request);
        Assert.DoesNotContain("classId=", request);
    }

    private static ModFileInfo PackageFile(string path, ResourceKey key) => new()
    {
        AbsolutePath = path,
        RelativePathInMod = Path.GetFileName(path),
        Kind = ModFileKind.Package,
        IsEnabled = true,
        SizeBytes = 0,
        Resources = new[] { new PackageResource(key, ChunkOffset: 0, StoredSize: 0, MemorySize: 0, CompressionType: 0) }
    };

    [Fact]
    public void ClassifyModReadsCategoryAndCasCategoryFromCatalogInfo()
    {
        var file = PackageFile("Hat.package", new ResourceKey(1, 2, 3));
        var mod = new ModEntry
        {
            Id = "hat", DisplayName = "Hat", AbsolutePath = "Hat.package", IsFolder = false,
            Files = new[] { file }, LastModifiedUtc = DateTime.UtcNow
        };
        var info = new Dictionary<ModFileInfo, PackageCatalogInfo>
        {
            [file] = new PackageCatalogInfo { Category = ContentCategory.Cas, CasCategory = CasCategory.Hair }
        };

        var (category, cas) = ModClassifier.ClassifyMod(mod, info);

        Assert.Equal(ContentCategory.Cas, category);
        Assert.Equal(CasCategory.Hair, cas);
    }

    [Fact]
    public void ClassifyModWithNoCatalogInfoFallsBackToScriptOrOther()
    {
        var scriptFile = new ModFileInfo
        {
            AbsolutePath = "mod.ts4script", RelativePathInMod = "mod.ts4script", Kind = ModFileKind.Script,
            IsEnabled = true, SizeBytes = 0
        };
        var mod = new ModEntry
        {
            Id = "mod", DisplayName = "Mod", AbsolutePath = "mod.ts4script", IsFolder = false,
            Files = new[] { scriptFile }, LastModifiedUtc = DateTime.UtcNow
        };

        var (category, cas) = ModClassifier.ClassifyMod(mod, new Dictionary<ModFileInfo, PackageCatalogInfo>());

        Assert.Equal(ContentCategory.Script, category);
        Assert.Equal(CasCategory.None, cas);
    }

    [Fact]
    public void ApplyInstallInstallsFreshFilesWithNoOldOnesAndCanBeUndone()
    {
        string modsFolder = Path.Combine(_root, "Mods");
        Directory.CreateDirectory(modsFolder);
        string download = Path.Combine(_root, "download.tmp");
        File.WriteAllText(download, "new mod contents");

        var journal = new ChangeJournal(Path.Combine(_root, "journal"));
        string id;
        string installedPath;
        using (var recorder = journal.Begin("install"))
        {
            var installed = CurseForgeUpdateChecker.ApplyInstall(recorder, modsFolder, download, "newmod.package");
            installedPath = Assert.Single(installed);
            Assert.Equal(Path.Combine(modsFolder, "newmod.package"), installedPath);
            id = recorder.Id;
        }
        Assert.Equal("new mod contents", File.ReadAllText(installedPath));

        Assert.True(journal.Undo(id).Success);
        Assert.False(File.Exists(installedPath));
    }
}
