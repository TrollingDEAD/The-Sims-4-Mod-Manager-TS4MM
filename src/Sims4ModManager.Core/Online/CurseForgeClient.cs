using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Online;

// The API sends null (not an empty list or string) for missing values, which would overwrite the
// defaults below - OnDeserialized puts them back.

public sealed class CurseForgeFile : IJsonOnDeserialized
{
    public long Id { get; set; }
    public long ModId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    /// <summary>1 release, 2 beta, 3 alpha.</summary>
    public int ReleaseType { get; set; }
    public DateTime FileDate { get; set; }
    public long FileLength { get; set; }
    public string? DownloadUrl { get; set; }
    public uint FileFingerprint { get; set; }
    public bool IsAvailable { get; set; } = true;
    public List<CurseForgeModule> Modules { get; set; } = new();
    /// <summary>Only reliably populated on a single/batch file lookup (GetFilesAsync) - re-fetch by
    /// id before trusting this rather than relying on what a mod/search listing happened to include.</summary>
    public List<CurseForgeFileDependency> Dependencies { get; set; } = new();

    void IJsonOnDeserialized.OnDeserialized()
    {
        DisplayName ??= string.Empty;
        FileName ??= string.Empty;
        Modules = Modules.OrEmpty();
        Dependencies = Dependencies.OrEmpty();
    }
}

public sealed class CurseForgeFileDependency
{
    public long ModId { get; set; }
    /// <summary>1 EmbeddedLibrary, 2 OptionalDependency, 3 RequiredDependency, 4 Tool,
    /// 5 Incompatible, 6 Include - per CurseForge's public docs, unverified against a live response.</summary>
    public int RelationType { get; set; }
}

public sealed class CurseForgeModule : IJsonOnDeserialized
{
    public string Name { get; set; } = string.Empty;
    public uint Fingerprint { get; set; }

    void IJsonOnDeserialized.OnDeserialized() => Name ??= string.Empty;
}

public sealed class CurseForgeLinks
{
    public string? WebsiteUrl { get; set; }
}

public sealed class CurseForgeAuthor : IJsonOnDeserialized
{
    public string Name { get; set; } = string.Empty;

    void IJsonOnDeserialized.OnDeserialized() => Name ??= string.Empty;
}

public sealed class CurseForgeMod : IJsonOnDeserialized
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public CurseForgeLinks Links { get; set; } = new();
    public List<CurseForgeAuthor> Authors { get; set; } = new();
    public List<CurseForgeFile> LatestFiles { get; set; } = new();
    public bool? AllowModDistribution { get; set; }
    public CurseForgeAsset? Logo { get; set; }
    public List<CurseForgeCategory> Categories { get; set; } = new();
    public int DownloadCount { get; set; }

    void IJsonOnDeserialized.OnDeserialized()
    {
        Name ??= string.Empty;
        Summary ??= string.Empty;
        Links ??= new();
        Authors = Authors.OrEmpty();
        LatestFiles = LatestFiles.OrEmpty();
        Categories = Categories.OrEmpty();
    }
}

public sealed class CurseForgeAsset
{
    public string? ThumbnailUrl { get; set; }
    public string? Url { get; set; }
}

public sealed class CurseForgeCategory : IJsonOnDeserialized
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public int ClassId { get; set; }
    public int? ParentCategoryId { get; set; }
    public bool IsClass { get; set; }

    void IJsonOnDeserialized.OnDeserialized() => Name ??= string.Empty;
}

public sealed class CurseForgePagination
{
    public int Index { get; set; }
    public int PageSize { get; set; }
    public int ResultCount { get; set; }
    public int TotalCount { get; set; }
}

public sealed class CurseForgeMatch : IJsonOnDeserialized
{
    /// <summary>Mod id.</summary>
    public long Id { get; set; }
    public CurseForgeFile File { get; set; } = new();
    public List<CurseForgeFile> LatestFiles { get; set; } = new();

    void IJsonOnDeserialized.OnDeserialized()
    {
        File ??= new();
        LatestFiles = LatestFiles.OrEmpty();
    }
}

public sealed class CurseForgeFingerprintResult : IJsonOnDeserialized
{
    public List<CurseForgeMatch> ExactMatches { get; set; } = new();
    public List<CurseForgeMatch> PartialMatches { get; set; } = new();
    public List<uint> UnmatchedFingerprints { get; set; } = new();

    void IJsonOnDeserialized.OnDeserialized()
    {
        ExactMatches = ExactMatches.OrEmpty();
        PartialMatches = PartialMatches.OrEmpty();
        UnmatchedFingerprints ??= new();
    }
}

internal static class CurseForgeJson
{
    /// <summary>A null list becomes empty; null elements are dropped.</summary>
    public static List<T> OrEmpty<T>(this List<T>? list) where T : class
    {
        if (list is null)
            return new List<T>();
        list.RemoveAll(x => x is null);
        return list;
    }
}

/// <summary>An error the user can act on (missing/invalid key, no connection, rate limit).</summary>
public sealed class CurseForgeException : Exception
{
    public CurseForgeException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Client for the official CurseForge API (api.curseforge.com). It needs a personal API key, which
/// CurseForge hands out for free at console.curseforge.com - the app deliberately ships none.
/// </summary>
public sealed class CurseForgeClient
{
    public const int Sims4GameId = 78062;
    public const string ApiKeyUrl = "https://console.curseforge.com/";
    private const int FingerprintBatch = 1000;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _http;

    public CurseForgeClient(string apiKey, HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: handler is null)
        {
            BaseAddress = new Uri("https://api.curseforge.com/"),
            Timeout = TimeSpan.FromSeconds(60)
        };
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey.Trim());
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    private sealed class DataEnvelope<T>
    {
        public T? Data { get; set; }
    }

    /// <summary>The search endpoint's envelope additionally carries paging info alongside the data.</summary>
    private sealed class PagedEnvelope<T>
    {
        public T? Data { get; set; }
        public CurseForgePagination? Pagination { get; set; }
    }

    /// <summary>Looks up files by fingerprint (in batches); exact and partial (file inside an archive) matches are merged.</summary>
    public async Task<CurseForgeFingerprintResult> MatchFingerprintsAsync(IReadOnlyCollection<uint> fingerprints, CancellationToken cancel = default)
    {
        var result = new CurseForgeFingerprintResult();
        foreach (var batch in fingerprints.Distinct().Chunk(FingerprintBatch))
        {
            var part = await PostAsync<CurseForgeFingerprintResult>($"v1/fingerprints/{Sims4GameId}", new { fingerprints = batch }, cancel);
            if (part is null)
                continue;
            result.ExactMatches.AddRange(part.ExactMatches);
            result.PartialMatches.AddRange(part.PartialMatches);
            result.UnmatchedFingerprints.AddRange(part.UnmatchedFingerprints);
        }
        return result;
    }

    public async Task<IReadOnlyList<CurseForgeMod>> GetModsAsync(IReadOnlyCollection<long> modIds, CancellationToken cancel = default)
    {
        var mods = new List<CurseForgeMod>();
        foreach (var batch in modIds.Distinct().Chunk(500))
            mods.AddRange((await PostAsync<List<CurseForgeMod>>("v1/mods", new { modIds = batch }, cancel)).OrEmpty());
        return mods;
    }

    /// <summary>Download link of a file; null if the author does not allow downloads outside CurseForge.</summary>
    public async Task<string?> GetDownloadUrlAsync(long modId, long fileId, CancellationToken cancel = default)
    {
        try
        {
            return await SendAsync<string>(new HttpRequestMessage(HttpMethod.Get, $"v1/mods/{modId}/files/{fileId}/download-url"), cancel);
        }
        catch (CurseForgeException ex) when (ex.InnerException is HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound })
        {
            return null;
        }
    }

    /// <summary>Downloads a file to <paramref name="destination"/>.</summary>
    public async Task DownloadAsync(string url, string destination, CancellationToken cancel = default)
    {
        try
        {
            // The download host is a CDN: the API key must not travel there.
            using var cdn = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var response = await cdn.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(output, cancel);
        }
        catch (HttpRequestException ex)
        {
            throw new CurseForgeException(L.F("Download fehlgeschlagen: {0}", ex.Message), ex);
        }
    }

    /// <summary>Full File objects (including Dependencies) for exact file ids - search/mod-list
    /// responses may carry a thinner shape, so installs re-fetch by id before trusting dependency data.</summary>
    public async Task<IReadOnlyList<CurseForgeFile>> GetFilesAsync(IReadOnlyCollection<long> fileIds, CancellationToken cancel = default)
    {
        var files = new List<CurseForgeFile>();
        foreach (var batch in fileIds.Distinct().Chunk(500))
            files.AddRange((await PostAsync<List<CurseForgeFile>>("v1/mods/files", new { fileIds = batch }, cancel)).OrEmpty());
        return files;
    }

    /// <summary>All categories for the given class (or every top-level class when <paramref name="classId"/> is null
    /// and <paramref name="classesOnly"/> is true - used once to discover the "Mods" class id for Sims 4).</summary>
    public async Task<IReadOnlyList<CurseForgeCategory>> GetCategoriesAsync(int? classId, bool classesOnly, CancellationToken cancel = default)
    {
        string path = $"v1/categories?gameId={Sims4GameId}"
            + (classId is { } id ? $"&classId={id}" : "")
            + (classesOnly ? "&classesOnly=true" : "");
        var (data, _) = await SendPagedAsync<List<CurseForgeCategory>>(new HttpRequestMessage(HttpMethod.Get, path), cancel);
        return data.OrEmpty();
    }

    /// <summary>1 Featured, 2 Popularity, 3 LastUpdated, 4 Name, 6 TotalDownloads - unverified against the live API.</summary>
    public enum SortField { Featured = 1, Popularity = 2, LastUpdated = 3, Name = 4, TotalDownloads = 6 }

    /// <summary>Searches/browses the Sims 4 catalog. <paramref name="searchFilter"/> may be null/empty to just
    /// browse a category by sort order.</summary>
    public async Task<(IReadOnlyList<CurseForgeMod> Mods, int TotalCount)> SearchModsAsync(
        string? searchFilter, int? classId, int? categoryId, SortField sortField, int index, int pageSize, CancellationToken cancel = default)
    {
        var query = new List<string>
        {
            $"gameId={Sims4GameId}", $"index={index}", $"pageSize={Math.Clamp(pageSize, 1, 50)}",
            $"sortField={(int)sortField}", "sortOrder=desc"
        };
        if (classId is { } cid) query.Add($"classId={cid}");
        if (categoryId is { } catId) query.Add($"categoryId={catId}");
        if (!string.IsNullOrWhiteSpace(searchFilter)) query.Add($"searchFilter={Uri.EscapeDataString(searchFilter.Trim())}");

        var (mods, pagination) = await SendPagedAsync<List<CurseForgeMod>>(
            new HttpRequestMessage(HttpMethod.Get, "v1/mods/search?" + string.Join('&', query)), cancel);
        return (mods.OrEmpty(), pagination?.TotalCount ?? 0);
    }

    private Task<T?> PostAsync<T>(string path, object body, CancellationToken cancel) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }, cancel);

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, CancellationToken cancel)
    {
        var envelope = await SendEnvelopeAsync<DataEnvelope<T>>(request, cancel);
        return envelope is null ? default : envelope.Data;
    }

    private async Task<(T? Data, CurseForgePagination? Pagination)> SendPagedAsync<T>(HttpRequestMessage request, CancellationToken cancel)
    {
        var envelope = await SendEnvelopeAsync<PagedEnvelope<T>>(request, cancel);
        return envelope is null ? (default, null) : (envelope.Data, envelope.Pagination);
    }

    private async Task<TEnvelope?> SendEnvelopeAsync<TEnvelope>(HttpRequestMessage request, CancellationToken cancel)
    {
        try
        {
            using (request)
            using (var response = await _http.SendAsync(request, cancel))
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new CurseForgeException(L.T("CurseForge lehnt den API-Schlüssel ab. Bitte den Schlüssel prüfen."));
                if ((int)response.StatusCode == 429)
                    throw new CurseForgeException(L.T("CurseForge meldet zu viele Anfragen. Bitte später erneut versuchen."));
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<TEnvelope>(Json, cancel);
            }
        }
        catch (HttpRequestException ex)
        {
            throw new CurseForgeException(L.F("CurseForge ist nicht erreichbar: {0}", ex.Message), ex);
        }
        catch (TaskCanceledException ex) when (!cancel.IsCancellationRequested)
        {
            throw new CurseForgeException(L.T("CurseForge antwortet nicht (Zeitüberschreitung)."), ex);
        }
        catch (JsonException ex)
        {
            throw new CurseForgeException(L.T("Unerwartete Antwort von CurseForge."), ex);
        }
    }
}
