using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BlockDock.Core;

namespace Island.Core;

public enum ContentKind { Mod, Shader, ResourcePack }
public sealed record ContentTarget(string GameDirectory, string InstanceId, string DisplayName, string Minecraft, string Loader,
    string RuntimeDirectory, VersionIsolation Isolation)
{
    public string Display => $"{DisplayName} · {Minecraft} · {(Loader == "Vanilla" ? "原版" : Loader)}";
    public string Key => Path.GetFullPath(GameDirectory).ToUpperInvariant() + "|" + InstanceId;
    public string ModLoader => Loader.ToLowerInvariant() switch { "cleanroom" => "forge", "vanilla" => "", var value => value };
}
public sealed record ContentFilter(ContentKind Kind, string Query = "", string Minecraft = "", string Loader = "", PackSort Sort = PackSort.Relevance);
public sealed record ContentFile(string ProjectId, string VersionId, ContentKind Kind, string Name, string FileName, string Url, long Size, string Sha1, string Sha512)
{
    public IReadOnlyList<ContentConflict> Incompatibilities { get; init; } = [];
}
public sealed record ContentConflict(string ProjectId, string VersionId);
public sealed record ContentPlan(ContentTarget Target, string Name, string? IconUrl, IReadOnlyList<ContentFile> Files)
{
    public IReadOnlyList<ContentConflict> Incompatibilities { get; init; } = [];
    public IReadOnlyList<string> TargetFiles => Files.Select(file => ContentDownloads.Destination(Target, file)).ToArray();
    public string Summary => $"安装到：{Target.Display}\n{Target.RuntimeDirectory}\n\n" + string.Join("\n", Files.Select(file => $"{ContentDownloads.Label(file.Kind)} · {file.FileName}"));
}
public interface IContentDownloads
{
    Task<CatalogSearchPage> SearchAsync(AppSettings settings, ContentFilter filter, int page, CancellationToken token);
    Task<IReadOnlyList<CatalogVersion>> GetVersionsAsync(AppSettings settings, CatalogProject project, ContentFilter filter, CancellationToken token);
    Task<ContentPlan> PrepareAsync(AppSettings settings, CatalogProject project, CatalogVersion version, ContentFilter filter, ContentTarget target, CancellationToken token);
    Task<string> InstallAsync(AppSettings settings, ContentPlan plan, IProgress<InstallProgress> progress, CancellationToken token);
}

public sealed class ContentDownloads(Func<AppSettings, DownloadClient>? networkFactory = null) : IContentDownloads
{
    private DownloadClient Network(AppSettings settings) => networkFactory?.Invoke(settings) ?? new(DownloadAvailability.Snapshot(settings));
    public static string Slug(ContentKind kind) => kind switch { ContentKind.Mod => "mod", ContentKind.Shader => "shader", ContentKind.ResourcePack => "resourcepack", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
    public static string Label(ContentKind kind) => kind switch { ContentKind.Mod => "Mod", ContentKind.Shader => "光影", ContentKind.ResourcePack => "资源包", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
    public static string Folder(ContentKind kind) => kind switch { ContentKind.Mod => "mods", ContentKind.Shader => "shaderpacks", ContentKind.ResourcePack => "resourcepacks", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
    public static string Destination(ContentTarget target, ContentFile file)
    {
        if (file.FileName.Length == 0 || file.FileName.Length > 220 || file.FileName != Path.GetFileName(file.FileName) || file.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("资源文件名无效。");
        return SafePaths.Under(target.RuntimeDirectory, Folder(file.Kind) + "/" + file.FileName);
    }
    private static string Query(string url, Dictionary<string, string> values) => url + (values.Count == 0 ? "" : "?" + string.Join('&', values.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))));
    private static string[] Strings(JsonObject data, string name) => (data[name] as JsonArray ?? []).Select(item => item?.GetValue<string>() ?? "").Where(item => item.Length > 0).ToArray();
    private static string Text(JsonNode? data, string key) => data?[key]?.GetValue<string>() ?? "";
    private static DateTimeOffset Date(JsonObject data, string key) => DateTimeOffset.TryParse(Text(data, key), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : DateTimeOffset.MinValue;
    private static CatalogVersion ParseVersion(JsonObject data) => new(Text(data, "id"), Text(data, "name"), Strings(data, "game_versions"), Strings(data, "loaders"), Date(data, "date_published"), Text(data, "version_type") switch { "release" => "正式版", "beta" => "测试版", "alpha" => "预览版", var channel => channel }, data);
    private static bool Matches(CatalogVersion version, ContentFilter filter) => (filter.Minecraft.Length == 0 || version.GameVersions.Contains(filter.Minecraft, StringComparer.OrdinalIgnoreCase))
        && (filter.Loader.Length == 0 || version.Loaders.Contains(filter.Loader, StringComparer.OrdinalIgnoreCase));
    public static string SearchUrl(ContentFilter filter, int page)
    {
        if (page < 0 || page > 10000 || filter.Query.Length > 200 || filter.Minecraft.Length > 100) throw new ArgumentException("搜索条件无效。");
        var facets = new List<string[]> { new[] { "project_type:" + Slug(filter.Kind) } };
        if (filter.Minecraft.Length > 0) facets.Add(["versions:" + filter.Minecraft]);
        if (filter.Loader.Length > 0) facets.Add(["categories:" + filter.Loader]);
        return Query("https://api.modrinth.com/v2/search", new() { ["query"] = filter.Query, ["facets"] = JsonSerializer.Serialize(facets), ["limit"] = "20", ["offset"] = (page * 20).ToString(CultureInfo.InvariantCulture),
            ["index"] = filter.Sort switch { PackSort.Downloads => "downloads", PackSort.Updated => "updated", PackSort.Newest => "newest", _ => "relevance" } });
    }
    public async Task<CatalogSearchPage> SearchAsync(AppSettings settings, ContentFilter filter, int page, CancellationToken token)
    {
        using var network = Network(settings); var data = await network.GetJsonAsync(SearchUrl(filter, page), token);
        var projects = (data["hits"] as JsonArray ?? []).OfType<JsonObject>().Select(hit => new CatalogProject(Text(hit, "project_id"), Text(hit, "title"), Text(hit, "description"), PackPlatform.Modrinth,
            hit["downloads"]?.GetValue<double>() ?? 0, Date(hit, "date_modified"), Date(hit, "date_created"), filter.Minecraft.Length > 0 ? [filter.Minecraft] : Strings(hit, "versions"),
            (filter.Loader.Length > 0 ? new[] { filter.Loader } : Strings(hit, "categories").Where(value => value is "fabric" or "forge" or "neoforge" or "quilt" or "iris" or "optifine")).Select(value => value switch { "fabric" => "Fabric", "forge" => "Forge", "neoforge" => "NeoForge", "quilt" => "Quilt", "iris" => "Iris", "optifine" => "OptiFine", _ => value }).ToArray(),
            "https://modrinth.com/" + Slug(filter.Kind) + "/" + Uri.EscapeDataString(Text(hit, "slug"))) { IconUrl = Text(hit, "icon_url"), Categories = Strings(hit, "categories") }).ToArray();
        var total = data["total_hits"]?.GetValue<long>() ?? 0; return new(projects, page, total, (page + 1) * 20 < total, []);
    }
    private static async Task<IReadOnlyList<CatalogVersion>> Versions(DownloadClient network, string id, ContentFilter filter, CancellationToken token)
    {
        var values = new Dictionary<string, string>();
        if (filter.Minecraft.Length > 0) values["game_versions"] = JsonSerializer.Serialize(new[] { filter.Minecraft });
        if (filter.Loader.Length > 0) values["loaders"] = JsonSerializer.Serialize(new[] { filter.Loader });
        var data = JsonNode.Parse(await network.GetTextAsync(Query("https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(id) + "/version", values), token)) as JsonArray ?? throw new InvalidDataException("发布列表无效。");
        return data.OfType<JsonObject>().Select(ParseVersion).Where(version => Matches(version, filter)).OrderByDescending(version => version.Published).ToArray();
    }
    public async Task<IReadOnlyList<CatalogVersion>> GetVersionsAsync(AppSettings settings, CatalogProject project, ContentFilter filter, CancellationToken token)
    {
        if (project.Platform != PackPlatform.Modrinth) throw new InvalidOperationException(DownloadAvailability.CurseForgeNotice);
        using var network = Network(settings); return await Versions(network, project.Id, filter, token);
    }
    private static void ValidateTarget(ContentTarget target)
    {
        SafePaths.InstanceName(target.InstanceId);
        var instance = InstanceCatalog.ListInstalled(target.GameDirectory).FirstOrDefault(item => item.Name == target.InstanceId) ?? throw new IOException("目标实例已不存在，请重新选择。");
        if (instance.Minecraft != target.Minecraft || !instance.Loader.Equals(target.Loader, StringComparison.OrdinalIgnoreCase)) throw new IOException("目标实例的游戏版本或加载器已改变，请重新选择。");
        var resolved = GameDirectories.Resolve(target.GameDirectory, target.InstanceId, target.Isolation).Directory;
        if (!GameFolder.SamePath(resolved, target.RuntimeDirectory)) throw new IOException("目标实例目录已改变，请重新选择。");
        _ = SafePaths.Under(target.RuntimeDirectory, ".island/content.json");
    }
    private static List<ContentFile> ReadInstalled(ContentTarget target)
    {
        var path = SafePaths.Under(target.RuntimeDirectory, ".island/content.json");
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("资源安装记录过大。");
        try { return JsonSerializer.Deserialize<List<ContentFile>>(File.ReadAllText(path)) ?? []; }
        catch (JsonException) { throw new InvalidDataException("资源安装记录损坏，请先检查 .island/content.json。"); }
    }
    private static void CheckInstalled(ContentTarget target, IEnumerable<ContentFile> requested, IEnumerable<ContentFile> installed)
    {
        foreach (var file in requested)
            foreach (var old in installed.Where(item => item.ProjectId == file.ProjectId && item.VersionId != file.VersionId))
                if (File.Exists(Destination(target, old))) throw new IOException($"已安装 {old.FileName}，请先移除或停用旧版本，再安装 {file.FileName}。");
    }
    private static void CheckIncompatible(ContentTarget target, IEnumerable<ContentFile> files, IEnumerable<ContentFile> installed, IEnumerable<ContentConflict> conflicts)
    {
        var active = installed.Where(file => File.Exists(Destination(target, file))).ToArray();
        if (conflicts.Concat(active.SelectMany(file => file.Incompatibilities)).Any(rule => files.Concat(active).Any(file =>
            rule.VersionId.Length > 0 ? file.VersionId == rule.VersionId : rule.ProjectId.Length > 0 && file.ProjectId == rule.ProjectId)))
            throw new InvalidDataException("资源声明与已安装资源或必需前置不兼容，请先处理冲突。");
    }
    private static void ValidateFile(ContentTarget target, ContentFile file)
    {
        _ = Destination(target, file);
        if (file.Sha512.Length != 128 || !file.Sha512.All(Uri.IsHexDigit) || file.Sha1.Length != 40 || !file.Sha1.All(Uri.IsHexDigit)
            || file.Size <= 0 || !Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !file.FileName.EndsWith(file.Kind == ContentKind.Mod ? ".jar" : ".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("资源文件、校验值或下载地址无效，请重新选择发布。");
    }
    public async Task<ContentPlan> PrepareAsync(AppSettings settings, CatalogProject project, CatalogVersion version, ContentFilter filter, ContentTarget target, CancellationToken token)
    {
        if (project.Platform != PackPlatform.Modrinth) throw new InvalidOperationException(DownloadAvailability.CurseForgeNotice);
        ValidateTarget(target);
        if (filter.Kind == ContentKind.Mod && target.ModLoader is not ("forge" or "fabric" or "neoforge" or "quilt")) throw new InvalidOperationException("该实例未安装受支持的 Mod 加载器，请先安装 Forge、Fabric、NeoForge 或 Quilt。");
        using var network = Network(settings);
        var installed = ReadInstalled(target);
        var files = new List<ContentFile>(); var selected = new Dictionary<string, string>(StringComparer.Ordinal); var incompatible = new List<ContentConflict>();
        async Task Resolve(string projectId, string versionId, ContentKind kind)
        {
            token.ThrowIfCancellationRequested();
            if (selected.TryGetValue(projectId, out var existing)) { if (existing != versionId) throw new InvalidDataException("必需前置要求了同一项目的不同版本，无法自动安装。"); return; }
            if (selected.Count >= 64) throw new InvalidDataException("必需前置过多，已停止自动解析。");
            selected[projectId] = versionId;
            var info = await network.GetJsonAsync("https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(projectId), token);
            if (Text(info, "project_type") != Slug(kind) || Text(info, "client_side") == "unsupported") throw new InvalidDataException("所选项目类型不匹配，或不支持客户端使用。");
            var data = await network.GetJsonAsync("https://api.modrinth.com/v2/version/" + Uri.EscapeDataString(versionId), token);
            var release = ParseVersion(data);
            var releaseConflicts = new List<ContentConflict>();
            var compatible = new ContentFilter(kind, Minecraft: target.Minecraft, Loader: kind == ContentKind.Mod ? target.ModLoader : kind == ContentKind.Shader ? filter.Loader : "");
            if (Text(data, "project_id") != projectId || release.Id != versionId || !Matches(release, compatible)) throw new InvalidDataException("所选发布或必需前置不兼容目标实例的游戏版本 / 加载器。");
            foreach (var dependency in (data["dependencies"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var type = Text(dependency, "dependency_type"); var depId = Text(dependency, "project_id"); var depVersion = Text(dependency, "version_id");
                if (type == "incompatible") { var conflict = new ContentConflict(depId, depVersion); incompatible.Add(conflict); releaseConflicts.Add(conflict); continue; }
                if (type != "required") continue;
                JsonObject? pinned = depVersion.Length > 0 ? await network.GetJsonAsync("https://api.modrinth.com/v2/version/" + Uri.EscapeDataString(depVersion), token) : null;
                depId = pinned is null ? depId : Text(pinned, "project_id");
                if (depId.Length == 0) throw new InvalidDataException("存在需要手动取得的外部必需前置：" + Text(dependency, "file_name"));
                var depInfo = await network.GetJsonAsync("https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(depId), token);
                var depKind = Text(depInfo, "project_type") switch { "mod" => ContentKind.Mod, "resourcepack" => ContentKind.ResourcePack, _ => throw new InvalidDataException("必需前置的项目类型暂不支持。") };
                if (pinned is null)
                {
                    var candidates = await Versions(network, depId, new(depKind, Minecraft: target.Minecraft, Loader: depKind == ContentKind.Mod ? target.ModLoader : ""), token);
                    var previous = selected.GetValueOrDefault(depId) ?? installed.FirstOrDefault(item => item.ProjectId == depId && File.Exists(Destination(target, item)))?.VersionId;
                    depVersion = (candidates.FirstOrDefault(item => item.Id == previous) ?? candidates.FirstOrDefault(item => item.Channel == "正式版") ?? candidates.FirstOrDefault())?.Id ?? throw new InvalidDataException("找不到兼容的必需前置：" + Text(depInfo, "title"));
                }
                await Resolve(depId, depVersion, depKind);
            }
            var extension = kind == ContentKind.Mod ? ".jar" : ".zip";
            var candidatesFiles = (data["files"] as JsonArray ?? []).OfType<JsonObject>().Where(item => Text(item, "filename").EndsWith(extension, StringComparison.OrdinalIgnoreCase) && Text(item, "file_type") is not ("sources-jar" or "dev-jar" or "javadoc-jar" or "signature")).ToArray();
            var artifact = candidatesFiles.FirstOrDefault(item => item["primary"]?.GetValue<bool>() == true) ?? candidatesFiles.FirstOrDefault() ?? throw new InvalidDataException("发布缺少可安装的 " + extension + " 文件。");
            var sha1 = Text(artifact["hashes"], "sha1"); var sha512 = Text(artifact["hashes"], "sha512");
            if (sha512.Length != 128 || !sha512.All(Uri.IsHexDigit) || sha1.Length != 40 || !sha1.All(Uri.IsHexDigit)) throw new InvalidDataException("资源缺少有效的 SHA-1 / SHA-512 校验值。");
            var url = Text(artifact, "url");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new InvalidDataException("资源下载链接必须为 HTTPS。");
            var file = new ContentFile(projectId, versionId, kind, Text(info, "title"), Text(artifact, "filename"), url, artifact["size"]?.GetValue<long>() ?? 0, sha1, sha512) { Incompatibilities = Array.AsReadOnly(releaseConflicts.ToArray()) };
            if (file.Size <= 0) throw new InvalidDataException("资源文件大小无效。");
            _ = Destination(target, file); files.Add(file);
        }
        await Resolve(project.Id, version.Id, filter.Kind);
        if (files.Select(file => Destination(target, file)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count) throw new InvalidDataException("资源和前置包含同名文件，无法自动安装。");
        CheckInstalled(target, files, installed); CheckIncompatible(target, files, installed, incompatible);
        return new(target, project.Title, project.IconUrl, Array.AsReadOnly(files.ToArray())) { Incompatibilities = Array.AsReadOnly(incompatible.ToArray()) };
    }
    public async Task<string> InstallAsync(AppSettings settings, ContentPlan plan, IProgress<InstallProgress> progress, CancellationToken token)
    {
        ValidateTarget(plan.Target);
        if (plan.Files.Count is 0 or > 64) throw new InvalidDataException("资源安装清单无效。");
        foreach (var file in plan.Files) ValidateFile(plan.Target, file);
        var statePath = SafePaths.Under(plan.Target.RuntimeDirectory, ".island/content.json"); Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        await using var guard = new FileStream(SafePaths.Under(plan.Target.RuntimeDirectory, ".island/content-install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var installed = ReadInstalled(plan.Target); CheckInstalled(plan.Target, plan.Files, installed);
        CheckIncompatible(plan.Target, plan.Files, installed, plan.Incompatibilities);
        using var network = Network(settings); var staged = new List<(ContentFile File, string Cache, string Target)>();
        for (var i = 0; i < plan.Files.Count; i++)
        {
            var file = plan.Files[i]; var target = Destination(plan.Target, file);
            var transfer = new TransferFile(target, [file.Url], file.Size, file.Sha1, file.Sha512);
            if (File.Exists(target))
            {
                if (!await DownloadClient.ValidAsync(target, transfer, token)) throw new IOException("已有同名文件且内容不同，未覆盖：" + file.FileName);
                continue;
            }
            var cache = SafePaths.Under(plan.Target.RuntimeDirectory, ".island/content-downloads/" + file.Sha512 + ".cache");
            await network.DownloadAsync(transfer with { Path = cache }, token, _ => progress.Report(new("下载资源", file.FileName, i, plan.Files.Count, network.Bytes, network.Speed)));
            staged.Add((file, cache, target)); progress.Report(new("校验资源", file.FileName, i + 1, plan.Files.Count, network.Bytes, network.Speed));
        }
        token.ThrowIfCancellationRequested(); ValidateTarget(plan.Target);
        var committed = new List<string>(); var temporary = SafePaths.Under(plan.Target.RuntimeDirectory, ".island/content.json.tmp");
        try
        {
            foreach (var item in staged)
            {
                token.ThrowIfCancellationRequested(); var target = Destination(plan.Target, item.File); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(item.Cache, target, false); committed.Add(target);
            }
            var updated = installed.Where(old => !plan.Files.Any(file => file.ProjectId == old.ProjectId)).Concat(plan.Files).ToArray();
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(updated, new JsonSerializerOptions { WriteIndented = true }), token);
            token.ThrowIfCancellationRequested(); File.Move(temporary, statePath, true);
        }
        catch
        {
            foreach (var path in committed) { try { File.Delete(path); } catch (IOException) { } }
            throw;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return $"已安装 {plan.Name}（含 {Math.Max(0, plan.Files.Count - 1)} 个必需前置）→ {plan.Target.RuntimeDirectory}";
    }
}
