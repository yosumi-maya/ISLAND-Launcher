using System.Globalization;
using System.Text.Json.Nodes;
using BlockDock.Core;

namespace Island.Core;

internal sealed class CurseForgeContentDownloads(AppSettings settings, DownloadClient network) : IDisposable
{
    private static int ClassId(ContentKind kind) => kind switch
    {
        ContentKind.Mod => 6, ContentKind.ResourcePack => 12, ContentKind.Shader => 6552,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    private static string Extension(ContentKind kind) => kind == ContentKind.Mod ? ".jar" : ".zip";
    private static string Identity(string id) => "curseforge:" + id;

    private static PackSearchFilter Filter(ContentFilter filter)
    {
        LoaderKind? loader = filter.Kind == ContentKind.Mod ? filter.Loader.ToLowerInvariant() switch
        {
            "forge" => LoaderKind.Forge, "fabric" => LoaderKind.Fabric, "neoforge" => LoaderKind.NeoForge,
            "" => null, _ => throw new InvalidOperationException("此 CurseForge 加载器筛选暂不支持，请选择全部加载器。")
        } : null;
        return new(filter.Query, filter.Minecraft, PackPlatform.CurseForge, loader, filter.Sort);
    }

    public Task<CatalogSearchPage> SearchAsync(ContentFilter filter, int page, CancellationToken token)
        => new ModpackCatalog(settings, network, ClassId(filter.Kind), Extension(filter.Kind)).SearchAsync(Filter(filter), page, token);

    public async Task<IReadOnlyList<CatalogVersion>> GetVersionsAsync(CatalogProject project, ContentFilter filter, CancellationToken token)
    {
        var catalog = new ModpackCatalog(settings, network, ClassId(filter.Kind), Extension(filter.Kind));
        var result = new List<CatalogVersion>();
        int? offset = 0;
        while (offset is { } current)
        {
            var page = await catalog.GetVersionsAsync(project, Filter(filter), current, token);
            result.AddRange(page.Versions); offset = page.NextOffset;
        }
        return result.DistinctBy(version => version.Id).OrderByDescending(version => version.Published).ToArray();
    }

    private async Task<JsonObject> ProjectAsync(string id, CancellationToken token)
    {
        if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
            throw new InvalidDataException("CurseForge 项目 ID 无效。");
        var project = (await network.GetJsonAsync($"https://api.curseforge.com/v1/mods/{id}", token, settings.CurseForgeApiKey)).Object("data");
        if (project.Long("gameId") != 432 || project.Long("id") != number) throw new InvalidDataException("项目不是 Minecraft 资源。");
        if (project["allowModDistribution"]?.GetValue<bool>() == false)
            throw new IOException("作者未开放第三方下载，请通过 CurseForge 官方项目页获取文件。");
        return project;
    }

    public async Task<ContentPlan> PrepareAsync(CatalogProject project, CatalogVersion version, ContentFilter filter, ContentTarget target, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(settings.CurseForgeApiKey)) throw new InvalidOperationException(DownloadAvailability.CurseForgeNotice);
        var files = new List<ContentFile>();
        var selected = new Dictionary<string, string>(StringComparer.Ordinal);
        var conflicts = new List<ContentConflict>();
        var rootId = project.Id;

        async Task Resolve(string projectId, string fileId, ContentKind kind)
        {
            token.ThrowIfCancellationRequested();
            if (selected.TryGetValue(projectId, out var previous))
            {
                if (previous != fileId) throw new InvalidDataException("CurseForge 前置声明了冲突的文件版本。");
                return;
            }
            if (selected.Count >= 64) throw new InvalidDataException("前置数量超过自动安装限制。");
            if (!long.TryParse(fileId, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
                throw new InvalidDataException("CurseForge 文件 ID 无效。");
            selected.Add(projectId, fileId);
            var info = await ProjectAsync(projectId, token);
            if (info.Long("classId") != ClassId(kind)) throw new InvalidDataException("CurseForge 项目类型与所选资源不符。");
            var data = (await network.GetJsonAsync($"https://api.curseforge.com/v1/mods/{projectId}/files/{fileId}", token, settings.CurseForgeApiKey)).Object("data");
            if (data.Long("id") != number || data.Long("modId")?.ToString(CultureInfo.InvariantCulture) != projectId
                || data["isAvailable"]?.GetValue<bool>() == false || data["isServerPack"]?.GetValue<bool>() == true)
                throw new InvalidDataException("CurseForge 文件已不可用或不属于所选项目。");
            var gameVersions = data.Array("gameVersions").Select(value => value?.GetValue<string>() ?? "").ToArray();
            if (!gameVersions.Contains(target.Minecraft, StringComparer.OrdinalIgnoreCase)
                || kind == ContentKind.Mod && !gameVersions.Contains(target.ModLoader, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("该 CurseForge 文件与目标实例的游戏版本或加载器不兼容。");
            var fileConflicts = new List<ContentConflict>();
            foreach (var dependency in data.Array("dependencies").OfType<JsonObject>())
            {
                var id = dependency.Long("modId")?.ToString(CultureInfo.InvariantCulture) ?? "";
                var relation = dependency.Long("relationType");
                if (relation == 5)
                {
                    var conflict = new ContentConflict(Identity(id), "");
                    conflicts.Add(conflict); fileConflicts.Add(conflict); continue;
                }
                if (relation != 3) continue;
                if (selected.ContainsKey(id)) continue;
                var dep = await ProjectAsync(id, token);
                var depKind = dep.Long("classId") switch
                {
                    6 => ContentKind.Mod, 12 => ContentKind.ResourcePack, 6552 => ContentKind.Shader,
                    _ => throw new InvalidDataException("必需前置类型暂不支持，请前往官方项目页确认。")
                };
                var depProject = ModpackCatalog.ParseCurseForgeProject(dep);
                var versions = await GetVersionsAsync(depProject,
                    new(depKind, Minecraft: target.Minecraft, Loader: depKind == ContentKind.Mod ? target.ModLoader : "")
                    { Platform = PackPlatform.CurseForge }, token);
                var chosen = versions.FirstOrDefault(item => item.Channel == "正式版") ?? versions.FirstOrDefault()
                    ?? throw new InvalidDataException("找不到兼容的必需前置：" + dep.Str("name"));
                await Resolve(id, chosen.Id, depKind);
            }
            var name = data.Str("fileName");
            if (!name.EndsWith(Extension(kind), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("文件不是可安装的资源类型。");
            var url = data.Str("downloadUrl");
            if (url.Length == 0)
                url = (await network.GetJsonAsync($"https://api.curseforge.com/v1/mods/{projectId}/files/{fileId}/download-url", token, settings.CurseForgeApiKey)).Str("data");
            var sha1 = data.Array("hashes").FirstOrDefault(hash => hash.Long("algo") == 1).Str("value");
            if (sha1.Length != 40 || !sha1.All(Uri.IsHexDigit) || data.Long("fileLength") is not > 0
                || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                throw new InvalidDataException("文件未提供可用的下载链接或 SHA-1 校验值，请前往官方项目页。");
            var file = new ContentFile(Identity(projectId), Identity(fileId), kind, info.Str("name"), name, url,
                data.Long("fileLength")!.Value, sha1, "")
            {
                Platform = PackPlatform.CurseForge, IsDependency = projectId != rootId,
                ProjectSlug = info.Str("slug"), ReleaseVersion = data.Str("displayName"),
                Incompatibilities = fileConflicts.ToArray()
            };
            _ = ContentDownloads.Destination(target, file);
            files.Add(file);
        }

        await Resolve(project.Id, version.Id, filter.Kind);
        if (files.Select(file => ContentDownloads.Destination(target, file)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw new InvalidDataException("资源与前置包含同名文件，请手动确认。");
        if (conflicts.Any(conflict => files.Any(file => file.ProjectId == conflict.ProjectId)))
            throw new InvalidDataException("资源与必需前置声明了不兼容关系。");
        return new(target, project.Title, project.IconUrl, files.ToArray()) { Incompatibilities = conflicts.ToArray() };
    }

    public void Dispose() => network.Dispose();
}
