using System.Net;
using System.Text;
using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Online;

namespace Sims4ModManager.Core.Tests;

public class PackageToolTests : IDisposable
{
    private readonly string _root;

    public PackageToolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "S4MM_PkgTools_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Write(string name, params TestDbpfBuilder.Entry[] entries)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, TestDbpfBuilder.Build(entries));
        return path;
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>Resource payloads (decompressed) by key, read back through the reader.</summary>
    private static Dictionary<ResourceKey, string> Content(string path)
    {
        var index = DbpfReader.TryReadIndex(path)!;
        return DbpfReader.TryReadResources(path, index)
            .Where(kv => kv.Key.Key.Type != PackageMerger.ManifestType)
            .ToDictionary(kv => kv.Key.Key, kv => Encoding.UTF8.GetString(kv.Value));
    }

    [Fact]
    public void MergeAndUnmergeRestoreTheOriginalFiles()
    {
        string a = Write("Hair A.package",
            new TestDbpfBuilder.Entry(0x034AEECB, 0, 1, Bytes("cas part A")),
            new TestDbpfBuilder.Entry(0x00B2D882, 0, 2, Bytes(new string('x', 2000)), TestDbpfBuilder.Zlib));
        string b = Write("Hair B.package",
            new TestDbpfBuilder.Entry(0x034AEECB, 0, 3, Bytes("cas part B")),
            new TestDbpfBuilder.Entry(0x034AEECB, 0, 1, Bytes("duplicate of A")));
        string merged = Path.Combine(_root, "Merged.package");

        var result = PackageMerger.Merge(new[] { a, b }, merged);

        Assert.Equal(3, result.ResourceCount);
        Assert.Equal(1, result.DuplicatesSkipped);
        var content = Content(merged);
        Assert.Equal("cas part A", content[new ResourceKey(0x034AEECB, 0, 1)]); // the first file wins
        Assert.Equal(new string('x', 2000), content[new ResourceKey(0x00B2D882, 0, 2)]); // compressed payload copied as is

        var manifest = PackageMerger.TryReadManifest(merged)!;
        Assert.Equal(new[] { "Hair A", "Hair B" }, manifest.Select(m => m.Name));
        Assert.Equal(2, manifest[0].Keys.Count);
        Assert.Single(manifest[1].Keys);

        var parts = PackageMerger.PlanUnmerge(merged)!;
        Assert.Equal(new[] { "Hair A.package", "Hair B.package" }, parts.Select(p => p.FileName));
        string outDir = Path.Combine(_root, "out");
        Directory.CreateDirectory(outDir);
        foreach (var part in parts)
            PackageMerger.WritePart(merged, part, Path.Combine(outDir, part.FileName));
        Assert.Equal(Content(a), Content(Path.Combine(outDir, "Hair A.package")));
        Assert.Equal("cas part B", Content(Path.Combine(outDir, "Hair B.package")).Single().Value);
    }

    [Fact]
    public void MergingAMergedPackageKeepsItsOriginalFileList()
    {
        string a = Write("A.package", new TestDbpfBuilder.Entry(0x034AEECB, 0, 1, Bytes("a")));
        string b = Write("B.package", new TestDbpfBuilder.Entry(0x034AEECB, 0, 2, Bytes("b")));
        string c = Write("C.package", new TestDbpfBuilder.Entry(0x034AEECB, 0, 3, Bytes("c")));
        string first = Path.Combine(_root, "first.package");
        PackageMerger.Merge(new[] { a, b }, first);
        string second = Path.Combine(_root, "second.package");
        PackageMerger.Merge(new[] { first, c }, second);

        Assert.Equal(new[] { "A", "B", "C" }, PackageMerger.TryReadManifest(second)!.Select(m => m.Name));
    }

    [Fact]
    public void ManifestMatchesTheSims4StudioLayout()
    {
        // Captured from a package merged by Sims 4 Studio: version 1, 8 zero bytes, count, UTF-8 name, keys as instance/type/group.
        byte[] s4s = Convert.FromHexString(
            "01000000" + "0000000000000000" + "01000000" +
            "04000000" + "E99DA241" + "01000000" +
            "0361E7E4BC9AD580" + "CBEE4A03" + "00000080");
        var entries = PackageMerger.TryParseManifest(s4s)!;
        Assert.Equal("面A", entries.Single().Name);
        Assert.Equal(new ResourceKey(0x034AEECB, 0x80000000, 0x80D59ABCE4E76103), entries[0].Keys.Single());
        Assert.Equal(s4s, PackageMerger.BuildManifest(entries));
    }

    [Fact]
    public void UnmergeNamesAreSafeAndUnique()
    {
        string merged = Path.Combine(_root, "m.package");
        byte[] manifest = PackageMerger.BuildManifest(new[]
        {
            new MergeManifestEntry("a:b", new[] { new ResourceKey(1, 0, 1) }),
            new MergeManifestEntry("A:B", new[] { new ResourceKey(1, 0, 2) }),
        });
        File.WriteAllBytes(merged, TestDbpfBuilder.Build(new[]
        {
            new TestDbpfBuilder.Entry(1, 0, 1, Bytes("1")),
            new TestDbpfBuilder.Entry(1, 0, 2, Bytes("2")),
            new TestDbpfBuilder.Entry(1, 0, 3, Bytes("not listed")),
            new TestDbpfBuilder.Entry(PackageMerger.ManifestType, 0, 0, manifest),
        }));

        var parts = PackageMerger.PlanUnmerge(merged)!;
        Assert.Equal(new[] { "a_b.package", "A_B (2).package", "m_Rest.package" }, parts.Select(p => p.FileName));
    }

    [Fact]
    public void OptimizeRemovesDuplicateEntriesAndCompressesWithoutChangingContent()
    {
        string text = string.Concat(Enumerable.Repeat("<Tuning>same text again</Tuning>\n", 200));
        string path = Write("big.package",
            new TestDbpfBuilder.Entry(0x0333406C, 0, 1, Bytes(text)),
            new TestDbpfBuilder.Entry(0x0333406C, 0, 2, Bytes("old")),
            new TestDbpfBuilder.Entry(0x0333406C, 0, 2, Bytes("new")),
            new TestDbpfBuilder.Entry(0x0333406C, 0, 3, Bytes("tiny")));

        var inspection = PackageTools.Inspect(DbpfReader.TryReadIndex(path)!);
        Assert.Equal(1, inspection.DuplicateEntries);
        Assert.Equal(1, inspection.UncompressedCount); // tiny resources are not worth it
        Assert.True(inspection.CanOptimize);

        string optimized = Path.Combine(_root, "optimized.package");
        var result = PackageTools.Optimize(path, optimized);

        Assert.Equal(1, result.DuplicatesRemoved);
        Assert.Equal(1, result.Compressed);
        Assert.True(result.Saved > 0);
        var index = DbpfReader.TryReadIndex(optimized)!;
        Assert.Equal(3, index.Count);
        Assert.Equal(DbpfReader.CompressionZlib, index.Single(r => r.Key.Instance == 1).CompressionType);
        var content = Content(optimized);
        Assert.Equal(text, content[new ResourceKey(0x0333406C, 0, 1)]);
        Assert.Equal("new", content[new ResourceKey(0x0333406C, 0, 2)]); // the last entry is the one kept
        Assert.False(PackageTools.Inspect(index).CanOptimize);
    }

    [Fact]
    public void EmptyPackagesAreRecognized()
    {
        Assert.True(PackageTools.IsEmpty(Array.Empty<PackageResource>()));
        Assert.True(PackageTools.IsEmpty(new[] { new PackageResource(new ResourceKey(PackageMerger.ManifestType, 0, 0), 96, 4, 4, 0) }));
        Assert.False(PackageTools.IsEmpty(new[] { new PackageResource(new ResourceKey(0x034AEECB, 0, 1), 96, 4, 4, 0) }));
    }

    [Fact]
    public void RecorderCreateCanBeUndone()
    {
        var journal = new ChangeJournal(Path.Combine(_root, "journal"));
        string target = Path.Combine(_root, "Mods", "Merged", "new.package");
        string id;
        using (var recorder = journal.Begin("create"))
        {
            recorder.Create(target, temp => File.WriteAllText(temp, "x"));
            id = recorder.Id;
            Assert.Throws<IOException>(() => recorder.Create(target, temp => File.WriteAllText(temp, "y")));
        }
        Assert.Equal("x", File.ReadAllText(target));
        Assert.True(journal.Undo(id).Success);
        Assert.False(File.Exists(target));
        Assert.False(Directory.Exists(Path.GetDirectoryName(target)));
    }

    // --- CurseForge --------------------------------------------------------------------------------

    /// <summary>Straightforward MurmurHash2 (seed 1) over the filtered bytes, as CurseForge documents it.</summary>
    private static uint ReferenceFingerprint(byte[] data)
    {
        var d = data.Where(b => b is not (9 or 10 or 13 or 32)).ToArray();
        const uint m = 0x5BD1E995;
        uint h = 1 ^ (uint)d.Length;
        int i = 0;
        for (; i + 4 <= d.Length; i += 4)
        {
            uint k = BitConverter.ToUInt32(d, i);
            k *= m; k ^= k >> 24; k *= m;
            h *= m; h ^= k;
        }
        switch (d.Length - i)
        {
            case 3: h ^= (uint)d[i + 2] << 16; goto case 2;
            case 2: h ^= (uint)d[i + 1] << 8; goto case 1;
            case 1: h ^= d[i]; h *= m; break;
        }
        h ^= h >> 13; h *= m; h ^= h >> 15;
        return h;
    }

    [Fact]
    public void FingerprintMatchesTheReferenceAlgorithmAndIgnoresWhitespace()
    {
        var random = new Random(7);
        foreach (int size in new[] { 0, 1, 2, 3, 4, 5, 100, 70_001, 200_003 })
        {
            var data = new byte[size];
            random.NextBytes(data);
            Assert.Equal(ReferenceFingerprint(data), CurseForgeFingerprint.Compute(data));
        }
        Assert.Equal(CurseForgeFingerprint.Compute(Bytes("abc def")), CurseForgeFingerprint.Compute(Bytes("a\tb\r\nc  def")));
    }

    private sealed class FakeCurseForge : HttpMessageHandler
    {
        public readonly List<string> Requests = new();
        public Func<HttpRequestMessage, string> Respond { get; set; } = _ => "{}";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery} key={string.Join(",", request.Headers.GetValues("x-api-key"))}");
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Respond(request), Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task UpdateCheckMatchesFilesByFingerprintAndFindsNewerReleases()
    {
        string mods = Path.Combine(_root, "Mods");
        string modFile = Path.Combine(mods, "Cool Mod", "coolmod.ts4script");
        Directory.CreateDirectory(Path.GetDirectoryName(modFile)!);
        File.WriteAllText(modFile, "script v1");
        File.WriteAllText(Path.Combine(mods, "other.package"), "unrelated");
        uint fp = CurseForgeFingerprint.Compute(modFile);

        var fake = new FakeCurseForge
        {
            Respond = r => r.RequestUri!.AbsolutePath switch
            {
                "/v1/fingerprints/78062" => $$$"""
                    {"data":{"exactMatches":[{"id":42,"file":{"id":100,"modId":42,"displayName":"Cool Mod 1.0","fileName":"coolmod.ts4script","releaseType":1,"fileDate":"2025-01-01T00:00:00Z","fileFingerprint":{{{fp}}},"isAvailable":true,"modules":[]},"latestFiles":[]}],"partialMatches":[],"unmatchedFingerprints":[1]}}
                    """,
                "/v1/mods" => """
                    {"data":[{"id":42,"name":"Cool Mod","links":{"websiteUrl":"https://www.curseforge.com/sims4/mods/cool-mod"},"authors":[{"name":"Author"}],
                      "latestFiles":[{"id":101,"modId":42,"displayName":"Cool Mod 1.1","fileName":"coolmod-1.1.zip","releaseType":1,"fileDate":"2025-06-01T00:00:00Z","isAvailable":true},
                                     {"id":102,"modId":42,"displayName":"Cool Mod 1.2 beta","fileName":"coolmod-1.2.zip","releaseType":2,"fileDate":"2025-07-01T00:00:00Z","isAvailable":true}]}]}
                    """,
                _ => "{}"
            }
        };
        var checker = new CurseForgeUpdateChecker(new CurseForgeClient("secret", fake), Path.Combine(_root, "cache"));
        var result = await checker.CheckAsync(ModScanner.Scan(mods));

        var mod = Assert.Single(result.Mods);
        Assert.Equal("Cool Mod", mod.Name);
        Assert.Equal(100, mod.InstalledFile.Id);
        Assert.Equal(101, mod.LatestFile!.Id); // releases are preferred over the newer beta
        Assert.True(mod.UpdateAvailable);
        Assert.Equal(modFile, mod.LocalFiles.Single().File.AbsolutePath);
        Assert.Equal(1, result.UnmatchedFiles);
        Assert.All(fake.Requests, r => Assert.EndsWith("key=secret", r));
        Assert.True(File.Exists(Path.Combine(_root, "cache", "fingerprints.json")));
    }

    [Fact]
    public async Task NullListsInTheApiResponseAreTreatedAsEmpty()
    {
        // The real API answers like this when nothing matches.
        var fake = new FakeCurseForge
        {
            Respond = r => r.RequestUri!.AbsolutePath switch
            {
                "/v1/fingerprints/78062" => """
                    {"data":{"isCacheBuilt":true,"exactMatches":[{"id":7,"file":{"id":1,"fileName":null,"modules":null,"fileFingerprint":5},"latestFiles":null}],
                      "exactFingerprints":[5],"partialMatches":null,"partialMatchFingerprints":{},"installedFingerprints":null,"unmatchedFingerprints":null}}
                    """,
                "/v1/mods" => """{"data":[{"id":7,"name":"X","links":null,"authors":null,"latestFiles":null}]}""",
                _ => "{}"
            }
        };
        var client = new CurseForgeClient("key", fake);

        var result = await client.MatchFingerprintsAsync(new uint[] { 5, 6 });
        var match = Assert.Single(result.ExactMatches);
        Assert.Empty(match.LatestFiles);
        Assert.Empty(match.File.Modules);
        Assert.Equal(string.Empty, match.File.FileName);
        Assert.Empty(result.PartialMatches);
        Assert.Empty(result.UnmatchedFingerprints);

        var mod = Assert.Single(await client.GetModsAsync(new long[] { 7 }));
        Assert.Null(mod.Links.WebsiteUrl);
        Assert.Empty(mod.Authors);
        Assert.Empty(mod.LatestFiles);
    }

    [Fact]
    public async Task RejectedApiKeyGivesAReadableError()
    {
        var fake = new FakeCurseForge { Status = HttpStatusCode.Forbidden };
        var client = new CurseForgeClient("wrong", fake);
        var error = await Assert.ThrowsAsync<CurseForgeException>(() => client.MatchFingerprintsAsync(new uint[] { 1 }));
        Assert.Contains("API-Schlüssel", error.Message);
    }

    [Fact]
    public void ApplyUpdateReplacesTheOldFilesAndCanBeUndone()
    {
        string folder = Path.Combine(_root, "Mods", "Cool Mod");
        Directory.CreateDirectory(folder);
        string old = Path.Combine(folder, "coolmod_v1.ts4script");
        File.WriteAllText(old, "v1");
        string download = Path.Combine(_root, "download.tmp");
        File.WriteAllText(download, "v2");

        var journal = new ChangeJournal(Path.Combine(_root, "journal"));
        string id;
        using (var recorder = journal.Begin("update"))
        {
            var installed = CurseForgeUpdateChecker.ApplyUpdate(recorder, new[] { old }, download, "coolmod_v2.ts4script");
            Assert.Equal(Path.Combine(folder, "coolmod_v2.ts4script"), installed.Single());
            id = recorder.Id;
        }
        Assert.False(File.Exists(old));
        Assert.Equal("v2", File.ReadAllText(Path.Combine(folder, "coolmod_v2.ts4script")));

        Assert.True(journal.Undo(id).Success);
        Assert.Equal("v1", File.ReadAllText(old));
        Assert.False(File.Exists(Path.Combine(folder, "coolmod_v2.ts4script")));
    }
}
