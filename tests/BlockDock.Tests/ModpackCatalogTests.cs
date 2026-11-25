using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BlockDock.Core;

static class ModpackCatalogTests
{
    private static CatalogProject Project(PackPlatform platform) => new(platform == PackPlatform.Modrinth ? "modrinth-project" : "123", "测试包", "说明", platform, 0, DateTimeOffset.MinValue, DateTimeOffset.MinValue, [], [], "");
    public static void AddTo(List<(string Name, Func<Task> Test)> tests, string root)
    {
        tests.Add(("双平台搜索保留整合包图标，缺图标时仍可返回结果", async () =>
        {
            var settings = new AppSettings { CurseForgeApiKey = "test" };
            using var network = new DownloadClient(settings, new FakeHandler(request => request.RequestUri!.Host == "api.modrinth.com"
                ? Json("""{"hits":[{"project_id":"mr","title":"MR","icon_url":"https://cdn.modrinth.com/test/icon.png","categories":["fabric"]},{"project_id":"empty","title":"Empty","icon_url":null}],"total_hits":2}""")
                : Json("""{"data":[{"id":123,"name":"CF","logo":{"thumbnailUrl":"https://media.forgecdn.net/test/thumb.png","url":"https://media.forgecdn.net/test/full.png"}},{"id":124,"name":"CF Full","logo":{"thumbnailUrl":"","url":"https://media.forgecdn.net/test/full.png"}}],"pagination":{"totalCount":2}}""")));
            var page = await new ModpackCatalog(settings, network).SearchAsync(new(), 0, default);
            string? Icon(CatalogProject project) => (string?)typeof(CatalogProject).GetProperty("IconUrl")?.GetValue(project);
            Check(Icon(page.Projects.Single(p => p.Id == "mr")) == "https://cdn.modrinth.com/test/icon.png", "Modrinth icon_url 应进入图标显示数据");
            Check(Icon(page.Projects.Single(p => p.Id == "123")) == "https://media.forgecdn.net/test/thumb.png", "CurseForge 应优先使用 logo.thumbnailUrl");
            Check(Icon(page.Projects.Single(p => p.Id == "124")) == "https://media.forgecdn.net/test/full.png", "缩略图为空时应使用完整图标");
            Check(Icon(page.Projects.Single(p => p.Id == "empty")) is null or "", "缺少图标不应丢弃项目");
        }));
        tests.Add(("Modrinth 名称、版本、加载器筛选按 AND 传给 API", async () =>
        {
            using var network = new DownloadClient(new(), new FakeHandler(request =>
            {
                var query = Query(request.RequestUri!);
                Check(query["query"] == "名字 & test", "encoded name");
                Check(query["offset"] == "20" && query["index"] == "updated", "page and sort");
                var facets = JsonNode.Parse(query["facets"])!.AsArray();
                Check(facets.Count == 3 && facets.All(f => f!.AsArray().Count == 1), "AND facet groups");
                Check(facets[1]![0]!.GetValue<string>() == "versions:1.20.1" && facets[2]![0]!.GetValue<string>() == "categories:fabric", "version and loader facets");
                return Json("""{"hits":[{"project_id":"abc","title":"包","description":"说明","downloads":20,"categories":["fabric"],"versions":["1.20.1"]}],"total_hits":41}""");
            }));
            var page = await new ModpackCatalog(new(), network).SearchAsync(new("名字 & test", "1.20.1", PackPlatform.Modrinth, LoaderKind.Fabric, PackSort.Updated), 1, default);
            Check(page.Projects.Count == 1 && page.HasNext && page.Total == 41, "pagination");
            Check(page.Projects[0].Compatibility == "Fabric  ·  1.20.1", "show the searched compatibility instead of unrelated latest versions");
        }));
        tests.Add(("CurseForge 搜索限定整合包并传正确加载器和 Key", async () =>
        {
            var settings = new AppSettings { CurseForgeApiKey = "test-session-key" };
            using var network = new DownloadClient(settings, new FakeHandler(request =>
            {
                var query = Query(request.RequestUri!);
                Check(query["gameId"] == "432" && query["classId"] == "4471", "Minecraft modpacks section");
                Check(query["gameVersion"] == "1.21.1" && query["modLoaderType"] == "6", "NeoForge enum");
                Check(query["sortField"] == "6" && query["index"] == "40", "sort and offset");
                Check(request.Headers.GetValues("x-api-key").Single() == settings.CurseForgeApiKey && !request.RequestUri!.AbsoluteUri.Contains(settings.CurseForgeApiKey), "header credential only");
                return Json("""{"data":[{"id":123,"name":"CF 包","summary":"说明","downloadCount":1234.0,"latestFilesIndexes":[{"gameVersion":"1.21.1","modLoader":6}]}],"pagination":{"totalCount":60}}""");
            }));
            var page = await new ModpackCatalog(settings, network).SearchAsync(new("", "1.21.1", PackPlatform.CurseForge, LoaderKind.NeoForge, PackSort.Downloads), 2, default);
            Check(page.Projects.Single().Platform == PackPlatform.CurseForge && !page.HasNext && page.Projects.Single().Loaders.Single() == "NeoForge", "CF mapped result");
        }));
        tests.Add(("全部平台无 Key 时仍显示 Modrinth 并明确提示", async () =>
        {
            using var network = new DownloadClient(new(), new FakeHandler(request =>
            {
                Check(request.RequestUri!.Host == "api.modrinth.com", "no unauthenticated CF request");
                return Json("""{"hits":[{"project_id":"abc","title":"MR 包"}],"total_hits":1}""");
            }));
            var page = await new ModpackCatalog(new(), network).SearchAsync(new(), 0, default);
            Check(page.Projects.Count == 1 && page.Notices.Single().Contains("CurseForge"), "partial results and key notice");
        }));
        tests.Add(("双平台分别分页并按下载量合并，项目 ID 不混淆", async () =>
        {
            var settings = new AppSettings { CurseForgeApiKey = "test" };
            using var network = new DownloadClient(settings, new FakeHandler(request => request.RequestUri!.Host == "api.modrinth.com"
                ? Json("""{"hits":[{"project_id":"123","title":"MR","downloads":10}],"total_hits":25}""")
                : Json("""{"data":[{"id":123,"name":"CF","downloadCount":20}],"pagination":{"totalCount":1}}""")));
            var page = await new ModpackCatalog(settings, network).SearchAsync(new(Sort: PackSort.Downloads), 0, default);
            Check(page.Projects.Count == 2 && page.Projects[0].Platform == PackPlatform.CurseForge && page.HasNext && page.Total == 26, "two distinct projects");
        }));
        tests.Add(("CurseForge 加载器筛选缺版本时不返回未筛选结果", async () =>
        {
            var settings = new AppSettings { CurseForgeApiKey = "test" };
            using var network = new DownloadClient(settings, new FakeHandler(_ => throw new Exception("Should not request an unfiltered catalog")));
            var page = await new ModpackCatalog(settings, network).SearchAsync(new(Platform: PackPlatform.CurseForge, Loader: LoaderKind.Forge), 0, default);
            Check(page.Projects.Count == 0 && page.Notices.Single().Contains("游戏版本"), "missing version notice");
        }));
        tests.Add(("Modrinth 发布列表继承筛选并排除不兼容版本", async () =>
        {
            using var network = new DownloadClient(new(), new FakeHandler(request =>
            {
                var query = Query(request.RequestUri!);
                Check(query["game_versions"] == "[\"1.20.1\"]" && query["loaders"] == "[\"fabric\"]", "filtered project versions URL");
                return Json("""[{"id":"a","name":"正确","game_versions":["1.20.1"],"loaders":["fabric"],"version_type":"release"},{"id":"b","name":"错误版本","game_versions":["1.21.1"],"loaders":["fabric"]},{"id":"c","game_versions":["1.20.1"],"loaders":["forge"]}]""");
            }));
            var page = await new ModpackCatalog(new(), network).GetVersionsAsync(Project(PackPlatform.Modrinth), new(GameVersion: "1.20.1", Loader: LoaderKind.Fabric), 0, default);
            Check(page.Versions.Single().Id == "a" && page.NextOffset is null, "compatible file only");
        }));
        tests.Add(("CurseForge 发布列表跳过服务端包、不可用文件并保留翻页", async () =>
        {
            var settings = new AppSettings { CurseForgeApiKey = "test" };
            using var network = new DownloadClient(settings, new FakeHandler(request =>
            {
                var query = Query(request.RequestUri!);
                Check(query["gameVersion"] == "1.20.1" && query["modLoaderType"] == "1" && query["index"] == "50", "CF version filters and next offset");
                return Json("""{"data":[{"id":1,"displayName":"客户端","fileName":"pack.zip","isAvailable":true,"gameVersions":["1.20.1","Forge"]},{"id":2,"fileName":"server.zip","isServerPack":true},{"id":3,"fileName":"hidden.zip","isAvailable":false}],"pagination":{"resultCount":3,"totalCount":99}}""");
            }));
            var page = await new ModpackCatalog(settings, network).GetVersionsAsync(Project(PackPlatform.CurseForge), new(GameVersion: "1.20.1", Loader: LoaderKind.Forge), 50, default);
            Check(page.Versions.Single().Id == "1" && page.NextOffset == 53, "client filter, raw API count for next page");
        }));
        tests.Add(("CurseForge ZIP 走官方文件链接、SHA-1 校验和导入识别", async () =>
        {
            var bytes = PackArchive("manifest.json", """{"manifestType":"minecraftModpack","manifestVersion":1,"name":"CF 测试","minecraft":{"version":"1.20.1","modLoaders":[]},"files":[],"overrides":"overrides"}""");
            var sha = Convert.ToHexString(SHA1.HashData(bytes));
            var settings = new AppSettings { CurseForgeApiKey = "test" };
            using var network = new DownloadClient(settings, new FakeHandler(request =>
            {
                if (request.RequestUri!.Host == "edge.forgecdn.net")
                {
                    Check(request.Headers.GetValues("x-api-key").Single() == "test", "Official CurseForge CDN carries the required API key");
                    return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                }
                Check(request.Headers.GetValues("x-api-key").Single() == "test", "API credential");
                if (request.RequestUri.AbsolutePath.EndsWith("/download-url")) return Json("""{"data":"https://edge.forgecdn.net/files/test.zip"}""");
                Check(request.RequestUri.AbsolutePath == "/v1/mods/123/files/456", "Uses file metadata without an extra project request");
                return Json(new JsonObject { ["data"] = new JsonObject { ["id"] = 456, ["modId"] = 123, ["fileName"] = "pack.zip", ["downloadUrl"] = null, ["fileLength"] = bytes.Length, ["hashes"] = new JsonArray(new JsonObject { ["algo"] = 1, ["value"] = sha }) } }.ToJsonString());
            }));
            var version = new CatalogVersion("456", "版本", [], [], DateTimeOffset.MinValue, "正式版", new());
            var path = await new ModpackCatalog(settings, network).DownloadArchiveAsync(Project(PackPlatform.CurseForge), version, Path.Combine(root, "cf-catalog"), default);
            Check((await ModpackInstaller.InspectAsync(path, default)).Format == PackFormat.CurseForge, "same installation preview flow");
        }));
        tests.Add(("Modrinth 下载优先有效 mrpack 而不是其他附件", async () =>
        {
            var bytes = PackArchive("modrinth.index.json", """{"formatVersion":1,"game":"minecraft","name":"MR 测试","dependencies":{"minecraft":"1.20.1"},"files":[]}""");
            var data = new JsonObject { ["files"] = new JsonArray(new JsonObject { ["primary"] = true, ["filename"] = "README.txt" }, new JsonObject
            {
                ["filename"] = "pack.mrpack", ["url"] = "https://cdn.modrinth.com/pack.mrpack", ["size"] = bytes.Length,
                ["hashes"] = new JsonObject { ["sha1"] = Convert.ToHexString(SHA1.HashData(bytes)), ["sha512"] = Convert.ToHexString(SHA512.HashData(bytes)) }
            }) };
            using var network = new DownloadClient(new(), new FakeHandler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
            // Production version data comes from parsed HTTP JSON, rather than JsonValue<int> fixtures.
            var version = new CatalogVersion("v1", "版本", [], [], DateTimeOffset.MinValue, "正式版", JsonNode.Parse(data.ToJsonString())!.AsObject());
            var path = await new ModpackCatalog(new(), network).DownloadArchiveAsync(Project(PackPlatform.Modrinth), version, Path.Combine(root, "mr-catalog"), default);
            Check((await ModpackInstaller.InspectAsync(path, default)).Name == "MR 测试", "mrpack chosen and imported");
        }));
        tests.Add(("已取消搜索不发布结果", async () =>
        {
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            using var network = new DownloadClient(new(), new FakeHandler(_ => throw new Exception("Should not request")));
            try { await new ModpackCatalog(new(), network).SearchAsync(new(), 0, cancel.Token); }
            catch (OperationCanceledException) { return; }
            throw new Exception("Expected canceled search");
        }));
    }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static Dictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));
    private static void Check(bool value, string message) { if (!value) throw new Exception("Catalog assertion failed: " + message); }
    private static byte[] PackArchive(string name, string json)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write(json);
        return buffer.ToArray();
    }
}
