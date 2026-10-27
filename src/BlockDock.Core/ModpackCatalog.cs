using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BlockDock.Core;

public enum PackPlatform { All, Modrinth, CurseForge }
public enum PackSort { Relevance, Downloads, Updated, Newest }
public sealed record PackSearchFilter(string Name = "", string GameVersion = "", PackPlatform Platform = PackPlatform.All,
    LoaderKind? Loader = null, PackSort Sort = PackSort.Relevance);
public sealed record CatalogProject(string Id, string Title, string Description, PackPlatform Platform, double Downloads,
    DateTimeOffset Updated, DateTimeOffset Created, IReadOnlyList<string> Versions, IReadOnlyList<string> Loaders, string Website)
{
    public string? IconUrl { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = [];
    public LoaderKind IconLoader => Loaders.Select(name => Enum.TryParse<LoaderKind>(name, true, out var loader) ? loader : LoaderKind.Vanilla).FirstOrDefault(loader => loader != LoaderKind.Vanilla);
    public string SourceLabel => Platform.ToString();
    public string SourceColor => Platform == PackPlatform.CurseForge ? "#CF6635" : "#268568";
    public string Badge => string.Concat(Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w[0])).ToUpperInvariant();
    public string Metadata => $"{(Downloads >= 10000 ? $"{Downloads / 10000:0.#} 万" : $"{Downloads:N0}")} 次下载" + (Updated > DateTimeOffset.MinValue ? $"  ·  {Updated:yyyy/MM/dd} 更新" : "");
    public string Compatibility => string.Join(" / ", Loaders) + (Versions.Count > 0 ? "  ·  " + string.Join(", ", Versions.TakeLast(4)) : "");
}
public sealed record CatalogSearchPage(IReadOnlyList<CatalogProject> Projects, int Page, long Total, bool HasNext, IReadOnlyList<string> Notices);
public sealed record CatalogVersion(string Id, string Name, IReadOnlyList<string> GameVersions, IReadOnlyList<string> Loaders,
    DateTimeOffset Published, string Channel, JsonObject Data)
{
    public string Display => $"{Name}  ·  {Channel}  ·  {string.Join(", ", GameVersions)}  ·  {string.Join(" / ", Loaders)}";
}
public sealed record CatalogVersionPage(IReadOnlyList<CatalogVersion> Versions, int? NextOffset);

public sealed class ModpackCatalog(AppSettings settings, DownloadClient network)
{
    public const int PageSize = 20;
    public const string CurseForgeKeyNotice = "CurseForge 需要 API Key，请在设置中填写后搜索。";
    private sealed record PlatformPage(IReadOnlyList<CatalogProject> Projects, long Total, bool HasNext, string? Notice = null);

    public async Task<CatalogSearchPage> SearchAsync(PackSearchFilter filter, int page, CancellationToken token)
    {
        if (page is < 0 or >= 500) throw new ArgumentOutOfRangeException(nameof(page));
        ValidateFilter(filter);
        var platforms = filter.Platform == PackPlatform.All ? new[] { PackPlatform.Modrinth, PackPlatform.CurseForge } : [filter.Platform];
        var results = await Task.WhenAll(platforms.Select(async platform =>
        {
            try
            {
                if (platform == PackPlatform.CurseForge)
                {
                    if (string.IsNullOrWhiteSpace(settings.CurseForgeApiKey)) return new PlatformPage([], 0, false, CurseForgeKeyNotice);
                    if (filter.Loader.HasValue && string.IsNullOrWhiteSpace(filter.GameVersion))
                        return new PlatformPage([], 0, false, "CurseForge 按加载器筛选时，还需选择游戏版本。");
                    var response = await network.GetJsonAsync(BuildCurseForgeSearchUrl(filter, page), token, settings.CurseForgeApiKey);
                    var projects = response.Array("data").OfType<JsonObject>().Select(ParseCurseForgeProject).ToList();
                    var total = (long)Number(response["pagination"], "totalCount");
                    return new PlatformPage(projects, total, (page + 1) * PageSize < Math.Min(total, 10000));
                }
                else
                {
                    var response = await network.GetJsonAsync(BuildModrinthSearchUrl(filter, page), token);
                    var projects = response.Array("hits").OfType<JsonObject>().Select(ParseModrinthProject).ToList();
                    var total = (long)Number(response, "total_hits");
                    return new PlatformPage(projects, total, (page + 1) * PageSize < total);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or HttpRequestException)
            { return new PlatformPage([], 0, false, $"{platform} 暂时不可用：{ex.Message}"); }
        }));
        token.ThrowIfCancellationRequested();
        // Relevance scores cannot be compared across providers. Interleave their own ranked results.
        IEnumerable<CatalogProject> merged = Enumerable.Range(0, results.Max(r => r.Projects.Count))
            .SelectMany(i => results.Where(r => r.Projects.Count > i).Select(r => r.Projects[i]));
        merged = filter.Sort switch
        {
            PackSort.Downloads => merged.OrderByDescending(p => p.Downloads),
            PackSort.Updated => merged.OrderByDescending(p => p.Updated),
            PackSort.Newest => merged.OrderByDescending(p => p.Created),
            _ => merged
        };
        var projectsOnPage = merged.Select(project => project with
        {
            Versions = string.IsNullOrWhiteSpace(filter.GameVersion) ? project.Versions : [filter.GameVersion.Trim()],
            Loaders = filter.Loader.HasValue ? [filter.Loader.Value.ToString()] : project.Loaders
        }).ToList();
        return new(projectsOnPage, page, results.Sum(r => r.Total), results.Any(r => r.HasNext), results.Select(r => r.Notice).OfType<string>().ToList());
    }

    public static string BuildModrinthSearchUrl(PackSearchFilter filter, int page)
    {
        var facets = new List<string[]> { new[] { "project_type:modpack" } };
        if (!string.IsNullOrWhiteSpace(filter.GameVersion)) facets.Add(["versions:" + filter.GameVersion.Trim()]);
        if (filter.Loader.HasValue) facets.Add(["categories:" + LoaderSlug(filter.Loader.Value)]);
        return Query("https://api.modrinth.com/v2/search", new()
        {
            ["query"] = filter.Name.Trim(), ["facets"] = JsonSerializer.Serialize(facets), ["limit"] = PageSize.ToString(CultureInfo.InvariantCulture),
            ["offset"] = (page * PageSize).ToString(CultureInfo.InvariantCulture),
            ["index"] = filter.Sort switch { PackSort.Downloads => "downloads", PackSort.Updated => "updated", PackSort.Newest => "newest", _ => "relevance" }
        });
    }

    public static string BuildCurseForgeSearchUrl(PackSearchFilter filter, int page)
    {
        var parameters = new Dictionary<string, string>
        {
            ["gameId"] = "432", ["classId"] = "4471", ["searchFilter"] = filter.Name.Trim(),
            ["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture), ["index"] = (page * PageSize).ToString(CultureInfo.InvariantCulture),
            ["sortField"] = filter.Sort switch { PackSort.Downloads => "6", PackSort.Updated => "3", PackSort.Newest => "11", _ => "2" }, ["sortOrder"] = "desc"
        };
        if (!string.IsNullOrWhiteSpace(filter.GameVersion)) parameters["gameVersion"] = filter.GameVersion.Trim();
        if (filter.Loader.HasValue) parameters["modLoaderType"] = CurseForgeLoader(filter.Loader.Value).ToString(CultureInfo.InvariantCulture);
        return Query("https://api.curseforge.com/v1/mods/search", parameters);
    }

    public async Task<CatalogVersionPage> GetVersionsAsync(CatalogProject project, PackSearchFilter filter, int offset, CancellationToken token)
    {
        ValidateFilter(filter);
        if (offset < 0 || offset >= 10000) throw new ArgumentOutOfRangeException(nameof(offset));
        var parameters = new Dictionary<string, string>();
        if (project.Platform == PackPlatform.Modrinth)
        {
            if (filter.GameVersion.Trim().Length > 0) parameters["game_versions"] = JsonSerializer.Serialize(new[] { filter.GameVersion.Trim() });
            if (filter.Loader.HasValue) parameters["loaders"] = JsonSerializer.Serialize(new[] { LoaderSlug(filter.Loader.Value) });
            var url = Query($"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(project.Id)}/version", parameters);
            var data = JsonNode.Parse(await network.GetTextAsync(url, token)) as JsonArray ?? throw new InvalidDataException("无效的 Modrinth 版本列表。");
            var versions = data.OfType<JsonObject>().Select(v => new CatalogVersion(v.Str("id"), v.Str("name"), Strings(v, "game_versions"), Strings(v, "loaders"), Date(v, "date_published"), Channel(v.Str("version_type")), v))
                .Where(v => Matches(v, filter)).OrderByDescending(v => v.Published).ToList();
            return new(versions, null);
        }
        RequireCurseForgeKey();
        _ = long.Parse(project.Id, CultureInfo.InvariantCulture);
        if (filter.GameVersion.Trim().Length > 0) parameters["gameVersion"] = filter.GameVersion.Trim();
        if (filter.Loader.HasValue) parameters["modLoaderType"] = CurseForgeLoader(filter.Loader.Value).ToString(CultureInfo.InvariantCulture);
        parameters["index"] = offset.ToString(CultureInfo.InvariantCulture); parameters["pageSize"] = "50";
        var response = await network.GetJsonAsync(Query($"https://api.curseforge.com/v1/mods/{project.Id}/files", parameters), token, settings.CurseForgeApiKey);
        var files = response.Array("data").OfType<JsonObject>().Where(v => v["isAvailable"]?.GetValue<bool>() != false && v["isServerPack"]?.GetValue<bool>() != true && v.Str("fileName").EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Select(v => new CatalogVersion(v["id"]!.ToString(), v.Str("displayName", v.Str("fileName")), Strings(v, "gameVersions").Where(IsGameVersion).ToList(),
                Strings(v, "gameVersions").Where(s => s is "Forge" or "Fabric" or "NeoForge" or "Quilt").ToList(), Date(v, "fileDate"), Number(v, "releaseType") switch { 2 => "测试版", 3 => "预览版", _ => "正式版" }, v))
            .Where(v => Matches(v, filter)).OrderByDescending(v => v.Published).ToList();
        int count = (int)Number(response["pagination"], "resultCount");
        if (response["pagination"]?["resultCount"] is null) count = response.Array("data").Count;
        long total = (long)Number(response["pagination"], "totalCount");
        int? next = count > 0 && offset + count < Math.Min(total, 10000) ? offset + count : null;
        return new(files, next);
    }

    public async Task<string> DownloadArchiveAsync(CatalogProject project, CatalogVersion version, string directory, CancellationToken token)
    {
        string url;
        long? size;
        string? sha1;
        string? sha512 = null;
        string extension;
        if (project.Platform == PackPlatform.Modrinth)
        {
            var files = version.Data.Array("files").OfType<JsonObject>().Where(f => f.Str("filename").EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)).ToList();
            var file = files.FirstOrDefault(f => f["primary"]?.GetValue<bool>() == true) ?? files.FirstOrDefault() ?? throw new InvalidDataException("此版本缺少 .mrpack 整合包文件。");
            url = file.Str("url"); size = file.Long("size"); sha1 = (file["hashes"]).Str("sha1"); sha512 = (file["hashes"]).Str("sha512"); extension = ".mrpack";
            if (sha1.Length != 40 || sha512.Length != 128) throw new InvalidDataException("整合包缺少有效的哈希校验值。");
        }
        else
        {
            RequireCurseForgeKey();
            _ = long.Parse(project.Id, CultureInfo.InvariantCulture); _ = long.Parse(version.Id, CultureInfo.InvariantCulture);
            var response = await network.GetJsonAsync($"https://api.curseforge.com/v1/mods/{project.Id}/files/{version.Id}", token, settings.CurseForgeApiKey);
            var file = response.Object("data");
            if (file["isServerPack"]?.GetValue<bool>() == true || file["isAvailable"]?.GetValue<bool>() == false || !file.Str("fileName").EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("此发布文件不是可用的客户端整合包 ZIP。");
            url = file.Str("downloadUrl");
            if (url.Length == 0)
            {
                var download = await network.GetJsonAsync($"https://api.curseforge.com/v1/mods/{project.Id}/files/{version.Id}/download-url", token, settings.CurseForgeApiKey);
                url = download.Str("data");
            }
            if (string.IsNullOrWhiteSpace(url)) throw new IOException("此整合包没有开放第三方下载。可从项目网站取得 ZIP 后导入。");
            size = file.Long("fileLength");
            sha1 = file.Array("hashes").FirstOrDefault(h => Number(h, "algo") == 1).Str("value"); extension = ".zip";
            if (sha1.Length != 40) throw new InvalidDataException("CurseForge 未提供有效的 SHA-1 校验值。");
        }
        var path = SafePaths.Under(directory, $"{project.Platform}-{SafePaths.InstanceName(project.Id)}-{SafePaths.InstanceName(version.Id)}{extension}");
        await network.DownloadAsync(new(path, [url], size, sha1, sha512), token);
        return path;
    }

    private static CatalogProject ParseModrinthProject(JsonObject data) => new(data.Str("project_id"), data.Str("title"), data.Str("description"), PackPlatform.Modrinth,
        Number(data, "downloads"), Date(data, "date_modified"), Date(data, "date_created"), Strings(data, "versions"), Strings(data, "categories").Where(s => s is "forge" or "fabric" or "neoforge").Select(DisplayLoader).ToList(),
        "https://modrinth.com/modpack/" + Uri.EscapeDataString(data.Str("slug", data.Str("project_id")))) { IconUrl = data.Str("icon_url"), Categories = Strings(data, "categories") };
    private static CatalogProject ParseCurseForgeProject(JsonObject data)
    {
        var indexes = data.Array("latestFilesIndexes");
        var thumbnail = data["logo"].Str("thumbnailUrl");
        return new CatalogProject(data["id"]!.ToString(), data.Str("name"), data.Str("summary"), PackPlatform.CurseForge, Number(data, "downloadCount"), Date(data, "dateModified"), Date(data, "dateCreated"),
            indexes.Select(v => v.Str("gameVersion")).Where(s => s.Length > 0).Distinct().ToList(), indexes.Select(v => Number(v, "modLoader") switch { 1 => "Forge", 4 => "Fabric", 6 => "NeoForge", _ => "" }).Where(s => s.Length > 0).Distinct().ToList(),
            (data["links"]).Str("websiteUrl")) { IconUrl = string.IsNullOrWhiteSpace(thumbnail) ? data["logo"].Str("url") : thumbnail };
    }
    private static bool Matches(CatalogVersion version, PackSearchFilter filter) =>
        (string.IsNullOrWhiteSpace(filter.GameVersion) || version.GameVersions.Contains(filter.GameVersion.Trim(), StringComparer.OrdinalIgnoreCase)) &&
        (!filter.Loader.HasValue || version.Loaders.Contains(LoaderSlug(filter.Loader.Value), StringComparer.OrdinalIgnoreCase));
    private void RequireCurseForgeKey() { if (string.IsNullOrWhiteSpace(settings.CurseForgeApiKey)) throw new InvalidOperationException(CurseForgeKeyNotice); }
    private static void ValidateFilter(PackSearchFilter filter)
    {
        if (filter.Loader == LoaderKind.Vanilla) throw new ArgumentException("整合包类型支持 Forge、Fabric 和 NeoForge，或选择全部类型。");
        if (filter.GameVersion.Length > 100 || filter.Name.Length > 200) throw new ArgumentException("搜索条件过长。");
    }
    private static string LoaderSlug(LoaderKind loader) => loader.ToString().ToLowerInvariant();
    private static int CurseForgeLoader(LoaderKind loader) => loader switch { LoaderKind.Forge => 1, LoaderKind.Fabric => 4, LoaderKind.NeoForge => 6, _ => throw new ArgumentException("不支持此加载器筛选。") };
    private static string DisplayLoader(string slug) => slug switch { "forge" => "Forge", "fabric" => "Fabric", "neoforge" => "NeoForge", _ => slug };
    private static bool IsGameVersion(string value) => value.Length > 0 && char.IsAsciiDigit(value[0]);
    private static string Channel(string channel) => channel switch { "release" => "正式版", "beta" => "测试版", "alpha" => "预览版", _ => channel };
    private static DateTimeOffset Date(JsonNode data, string key) => DateTimeOffset.TryParse(data.Str(key), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : DateTimeOffset.MinValue;
    private static IReadOnlyList<string> Strings(JsonNode data, string key) => data.Array(key).Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList();
    private static double Number(JsonNode? node, string key) => double.TryParse(node?[key]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0;
    private static string Query(string endpoint, Dictionary<string, string> parameters) => endpoint + (parameters.Count == 0 ? "" : "?" + string.Join('&', parameters.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value))));
}
