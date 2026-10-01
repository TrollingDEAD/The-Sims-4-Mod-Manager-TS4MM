using Sims4ModManager.Core.Online;

namespace Sims4ModManager.Core.Tests;

public class CurseForgeDependencyResolverTests
{
    private static string Body(HttpRequestMessage request) => request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";

    private static CurseForgeMod Mod(long id, bool allowDistribution = true) =>
        new() { Id = id, Name = $"Mod {id}", AllowModDistribution = allowDistribution };

    private static CurseForgeFile File(long id, long modId, params (long ModId, int RelationType)[] dependencies) => new()
    {
        Id = id,
        ModId = modId,
        FileName = $"file{id}.package",
        IsAvailable = true,
        Dependencies = dependencies.Select(d => new CurseForgeFileDependency { ModId = d.ModId, RelationType = d.RelationType }).ToList()
    };

    [Fact]
    public async Task SingleRootWithNoDependenciesResolvesToJustItself()
    {
        var rootFile = File(100, modId: 1);
        var fake = new FakeCurseForge { Respond = r => Body(r).Contains("\"fileIds\":[100]") ? Wrap(rootFile) : "{}" };
        var client = new CurseForgeClient("key", fake);

        var result = await CurseForgeDependencyResolver.ResolveAsync(client, new[] { (Mod(1), rootFile) });

        var entry = Assert.Single(result.Chain);
        Assert.True(entry.IsRoot);
        Assert.Equal(1, entry.ModId);
        Assert.Empty(result.Blocked);
    }

    [Fact]
    public async Task RequiredDependencyIsPulledInButEmbeddedLibraryIsNot()
    {
        var rootFile = File(100, modId: 1, (2, 3), (3, 1)); // 2 required, 3 embedded library (not pulled in)
        var depFile = File(200, modId: 2);
        // The dependency mod's "best file" must come back from GetModsAsync as LatestFiles.
        var depMod = Mod(2);
        depMod.LatestFiles.Add(depFile);

        var fake = new FakeCurseForge
        {
            Respond = r =>
            {
                string body = Body(r);
                if (r.RequestUri!.AbsolutePath == "/v1/mods/files")
                    return body.Contains("100") ? Wrap(rootFile) : Wrap(depFile);
                if (r.RequestUri.AbsolutePath == "/v1/mods")
                    return Wrap(new[] { depMod });
                return "{}";
            }
        };
        var client = new CurseForgeClient("key", fake);

        var result = await CurseForgeDependencyResolver.ResolveAsync(client, new[] { (Mod(1), rootFile) });

        Assert.Equal(2, result.Chain.Count);
        Assert.Contains(result.Chain, c => c.IsRoot && c.ModId == 1);
        Assert.Contains(result.Chain, c => !c.IsRoot && c.ModId == 2);
        Assert.DoesNotContain(result.Chain, c => c.ModId == 3);
    }

    [Fact]
    public async Task TwoRootsSharingADependencyOnlyResolveItOnce()
    {
        var rootFileA = File(100, modId: 1, (3, 3));
        var rootFileB = File(101, modId: 2, (3, 3));
        var depFile = File(300, modId: 3);
        var depMod = Mod(3);
        depMod.LatestFiles.Add(depFile);

        var fake = new FakeCurseForge
        {
            Respond = r =>
            {
                string body = Body(r);
                if (r.RequestUri!.AbsolutePath == "/v1/mods/files")
                {
                    if (body.Contains("100")) return Wrap(rootFileA);
                    if (body.Contains("101")) return Wrap(rootFileB);
                    return Wrap(depFile);
                }
                if (r.RequestUri.AbsolutePath == "/v1/mods")
                    return Wrap(new[] { depMod });
                return "{}";
            }
        };
        var client = new CurseForgeClient("key", fake);

        var result = await CurseForgeDependencyResolver.ResolveAsync(client, new[] { (Mod(1), rootFileA), (Mod(2), rootFileB) });

        Assert.Equal(3, result.Chain.Count); // two roots + the shared dependency once
        Assert.Single(result.Chain, c => c.ModId == 3);
    }

    [Fact]
    public async Task ModDisallowingDistributionIsReportedAsBlocked()
    {
        var rootFile = File(100, modId: 1);
        var fake = new FakeCurseForge { Respond = r => Body(r).Contains("100") ? Wrap(rootFile) : "{}" };
        var client = new CurseForgeClient("key", fake);

        var result = await CurseForgeDependencyResolver.ResolveAsync(client, new[] { (Mod(1, allowDistribution: false), rootFile) });

        var blocked = Assert.Single(result.Blocked);
        Assert.Equal(1, blocked.Id);
        Assert.Single(result.Chain); // still part of the chain - the caller decides whether to refuse
    }

    private static string FileJson(CurseForgeFile file)
    {
        string deps = string.Join(",", file.Dependencies.Select(d =>
            "{\"modId\":" + d.ModId + ",\"relationType\":" + d.RelationType + "}"));
        return "{\"id\":" + file.Id + ",\"modId\":" + file.ModId + ",\"fileName\":\"" + file.FileName +
               "\",\"isAvailable\":true,\"dependencies\":[" + deps + "]}";
    }

    private static string Wrap(CurseForgeFile file) => "{\"data\":[" + FileJson(file) + "]}";

    private static string Wrap(IReadOnlyList<CurseForgeMod> mods)
    {
        string modsJson = string.Join(",", mods.Select(m =>
        {
            string allow = m.AllowModDistribution is { } a ? a.ToString().ToLowerInvariant() : "null";
            string files = string.Join(",", m.LatestFiles.Select(FileJson));
            return "{\"id\":" + m.Id + ",\"name\":\"" + m.Name + "\",\"allowModDistribution\":" + allow +
                   ",\"latestFiles\":[" + files + "]}";
        }));
        return "{\"data\":[" + modsJson + "]}";
    }
}
