using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BlockDock.Core;

if (JavaProbeFixture.TryRun(args)) return;
if (args.Length == 2 && args[0] == "--java-scan")
{
    var found = await JavaDiscovery.DiscoverAsync(JavaSearchPaths.ForGameDirectory(args[1], ""), default);
    foreach (var java in found) Console.WriteLine(java.Display);
    Check(found.Any(java => java.Is64Bit), "live 64-bit Java discovered");
    Console.WriteLine("JAVA SCAN PASSED");
    return;
}
if (args.Contains("--catalog-network"))
{
    var options = new AppSettings();
    using var network = new DownloadClient(options);
    var catalog = new ModpackCatalog(options, network);
    var filter = new PackSearchFilter("Fabulously Optimized", "1.20.1", PackPlatform.All, LoaderKind.Fabric, PackSort.Downloads);
    var page = await catalog.SearchAsync(filter, 0, default);
    Check(page.Projects.Count > 0 && page.Notices.Any(n => n.Contains("CurseForge")), "live mixed search and CF notice");
    var project = page.Projects.First(p => p.Title.Equals("Fabulously Optimized", StringComparison.OrdinalIgnoreCase));
    var releases = await catalog.GetVersionsAsync(project, filter, 0, default);
    Check(releases.Versions.Count > 0 && releases.Versions.All(v => v.GameVersions.Contains("1.20.1") && v.Loaders.Contains("fabric")), "filtered releases");
    var release = releases.Versions.First(v => v.Channel == "正式版");
    var path = await catalog.DownloadArchiveAsync(project, release, Path.GetFullPath("artifacts/catalog-network"), default);
    var pack = await ModpackInstaller.InspectAsync(path, default);
    Check(pack.MinecraftVersion == "1.20.1" && pack.Loader == LoaderKind.Fabric, "downloaded preview matches filter");
    var next = await catalog.SearchAsync(new(GameVersion: "1.20.1", Platform: PackPlatform.Modrinth, Loader: LoaderKind.Fabric, Sort: PackSort.Downloads), 1, default);
    Check(next.Page == 1 && next.Projects.Count > 0, "live next page");
    Console.WriteLine($"CATALOG NETWORK PASSED: {project.Title}, {releases.Versions.Count} filtered versions, {pack.MinecraftVersion}, {pack.Loader}; page 2: {next.Projects.Count} results");
    return;
}
if (args.Contains("--pack"))
{
    var path = args[System.Array.IndexOf(args, "--pack") + 1];
    var options = new AppSettings { GameDirectory = Path.GetFullPath("artifacts/live-install/.minecraft"), Source = DownloadSource.Mirror, ParallelDownloads = 16 };
    var pack = await ModpackInstaller.InspectAsync(path, default);
    Console.WriteLine($"Pack: {pack.Name}, {pack.MinecraftVersion}, {pack.Loader} {pack.LoaderVersion}, {pack.FileCount} files");
    using var net = new DownloadClient(options);
    var result = await new MinecraftInstaller(options, net).InstallAsync(new(pack.MinecraftVersion, "Smoke-Modpack", pack.Loader, pack.LoaderVersion), new ThrottledProgress(), default, pack);
    Check(Directory.GetFiles(Path.Combine(result, "mods"), "*.jar").Length > 0, "Downloaded mod jars");
    Check(Directory.Exists(Path.Combine(result, "config")), "Extracted overrides");
    Console.WriteLine("MODPACK INSTALL PASSED: " + result);
    return;
}
if (args.Contains("--network"))
{
    var options = new AppSettings { GameDirectory = Path.GetFullPath("artifacts/network-check"), Source = DownloadSource.Mirror, ParallelDownloads = 16 };
    using var net = new DownloadClient(options);
    var service = new MinecraftInstaller(options, net);
    var versions = await service.GetVersionsAsync(default);
    Console.WriteLine($"Manifest: {versions.Count} versions, latest release {versions.First(v => v.Type == "release").Id}");
    foreach (var loader in new[] { LoaderKind.Fabric, LoaderKind.Forge, LoaderKind.NeoForge })
    {
        var mc = loader == LoaderKind.NeoForge ? "1.21.1" : "1.20.1";
        var list = await service.GetLoaderVersionsAsync(loader, mc, default);
        Check(list.Count > 0, loader + " live versions");
        Console.WriteLine($"{loader} / {mc}: {list.Count} versions, first {list.First().Version}");
    }
    var json = await service.GetVersionJsonAsync("1.20.1", default);
    Check(json.Str("id") == "1.20.1", "Version metadata SHA-1");
    Console.WriteLine("NETWORK CHECKS PASSED");
    return;
}
if (args.Contains("--install"))
{
    int index = System.Array.IndexOf(args, "--install");
    var loader = Enum.Parse<LoaderKind>(args[index + 1], true);
    var mc = args[index + 2];
    var loaderVersion = args.Length > index + 3 ? args[index + 3] : null;
    var root = Path.GetFullPath("artifacts/live-install/.minecraft");
    var options = new AppSettings { GameDirectory = root, Source = DownloadSource.Mirror, ParallelDownloads = 16 };
    using var net = new DownloadClient(options);
    var service = new MinecraftInstaller(options, net);
    var request = new InstallRequest(mc, "Smoke-" + loader + "-" + mc, loader, loaderVersion);
    var progress = new ThrottledProgress();
    var result = await service.InstallAsync(request, progress, default);
    var file = Path.Combine(result, request.InstanceName + ".json");
    Check(File.Exists(file), "Committed version JSON");
    Check(JsonNode.Parse(File.ReadAllText(file))!.Str("id") == request.InstanceName, "Committed version id");
    Console.WriteLine("INSTALL PASSED: " + result);
    return;
}

var tests = new List<(string Name, Func<Task> Test)>();
string testRoot = Path.Combine(Path.GetTempPath(), "BlockDockTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
tests.Add(("拒绝路径穿越、绝对路径与 ADS", () =>
{
    foreach (var path in new[] { "../escape", "/escape", "C:/escape", "mods/a.jar:evil", "mods/../evil", "mods/abc. /evil", "\\server\\share" })
        Throws<InvalidDataException>(() => SafePaths.Under(testRoot, path));
    Check(SafePaths.Under(testRoot, "mods/正常.jar").StartsWith(testRoot), "normal path");
    return Task.CompletedTask;
}));
tests.Add(("拒绝 Windows 保留版本名称", () =>
{
    foreach (var name in new[] { "CON", "NUL.zip", "COM1", "../abc", "a/b", "", " test", "abc." }) Throws<ArgumentException>(() => SafePaths.InstanceName(name));
    Check(SafePaths.InstanceName("我的世界-1.20.1") == "我的世界-1.20.1", "Unicode name");
    return Task.CompletedTask;
}));
tests.Add(("Maven 坐标与分类器解析", () =>
{
    Check(SafePaths.Maven("a.b:c:1.2:all@zip") == "a/b/c/1.2/c-1.2-all.zip", "classifier");
    Throws<InvalidDataException>(() => SafePaths.Maven("oops")); return Task.CompletedTask;
}));
tests.Add(("Windows 库规则及 natives", () =>
{
    var json = JsonNode.Parse("""{"libraries":[{"name":"a:b:1","rules":[{"action":"allow","os":{"name":"linux"}}]},{"name":"a:c:1","downloads":{"artifact":{"path":"a/c/1/c-1.jar","url":"https://example.org/a"},"classifiers":{"natives-windows":{"path":"a/c/1/native.jar","url":"https://example.org/n"}}},"natives":{"windows":"natives-windows"}}]}""")!.AsObject();
    var files = MinecraftInstaller.LibraryFiles(json, testRoot).ToList();
    Check(files.Count == 2 && files.Any(f => f.Path.EndsWith("native.jar")), "Windows and native selection");
    return Task.CompletedTask;
}));
tests.Add(("NeoForge 与游戏版本精确匹配", () =>
{
    Check(MinecraftInstaller.NeoMatches("21.1.10", "1.21.1"), "21.1");
    Check(!MinecraftInstaller.NeoMatches("21.10.0", "1.21.1"), "not 21.10");
    Check(MinecraftInstaller.NeoMatches("21.0.2-beta", "1.21"), "21.0"); return Task.CompletedTask;
}));

byte[] payload = Encoding.UTF8.GetBytes("BlockDock verified download 中文测试 payload");
string sha = Convert.ToHexString(SHA1.HashData(payload));
tests.Add(("断点续传使用 Range 并验证 SHA-1", async () =>
{
    var path = Path.Combine(testRoot, "resume.bin");
    await File.WriteAllBytesAsync(path + ".part", payload[..8]);
    bool ranged = false;
    using var network = new DownloadClient(new(), new FakeHandler(request =>
    {
        var start = request.Headers.Range?.Ranges.Single().From;
        ranged = start == 8;
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(payload[(int)start!.Value..]) };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start.Value, payload.Length - 1, payload.Length);
        return response;
    }));
    await network.DownloadAsync(new(path, ["https://test.invalid/file"], payload.Length, sha), default);
    Check(ranged && File.ReadAllBytes(path).SequenceEqual(payload), "Range result");
}));
tests.Add(("服务端忽略 Range 时从头重写", async () =>
{
    var path = Path.Combine(testRoot, "ignored-range.bin");
    await File.WriteAllTextAsync(path + ".part", "BAD");
    using var network = new DownloadClient(new(), new FakeHandler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }));
    await network.DownloadAsync(new(path, ["https://test.invalid/file"], payload.Length, sha), default);
    Check(File.ReadAllBytes(path).SequenceEqual(payload), "No duplicate prefix");
}));
tests.Add(("错误哈希触发重试且不发布坏文件", async () =>
{
    var path = Path.Combine(testRoot, "retry-hash.bin"); int calls = 0;
    using var network = new DownloadClient(new(), new FakeHandler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(++calls == 1 ? Encoding.UTF8.GetBytes("bad") : payload) }));
    await network.DownloadAsync(new(path, ["https://test.invalid/file"], payload.Length, sha), default);
    Check(calls == 2 && File.ReadAllBytes(path).SequenceEqual(payload), "Hash retry");
}));
tests.Add(("官方失败自动回退镜像", async () =>
{
    var path = Path.Combine(testRoot, "mirror.bin");
    using var network = new DownloadClient(new(), new FakeHandler(req => new(req.RequestUri!.Host == "bmclapi2.bangbang93.com" ? HttpStatusCode.OK : HttpStatusCode.Forbidden) { Content = new ByteArrayContent(payload) }));
    await network.DownloadAsync(new(path, ["https://libraries.minecraft.net/test.jar"], payload.Length, sha), default);
    Check(File.Exists(path), "Mirror fallback");
}));
tests.Add(("已有文件校验失败后自动修复", async () =>
{
    var path = Path.Combine(testRoot, "repair.bin"); await File.WriteAllTextAsync(path, "corrupt");
    using var network = new DownloadClient(new(), new FakeHandler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }));
    await network.DownloadAsync(new(path, ["https://test.invalid/file"], payload.Length, sha), default);
    Check(File.ReadAllBytes(path).SequenceEqual(payload), "repaired");
}));
tests.Add(("取消下载不提交最终文件", async () =>
{
    var path = Path.Combine(testRoot, "cancel.bin"); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    using var network = new DownloadClient(new(), new FakeHandler(_ => throw new Exception("Should not request")));
    await ThrowsAsync<OperationCanceledException>(() => network.DownloadAsync(new(path, ["https://test.invalid/file"]), cancellation.Token));
    Check(!File.Exists(path), "not committed");
}));
tests.Add(("传输中取消后保留分片并续传", async () =>
{
    var data = Enumerable.Range(0, 600000).Select(i => (byte)(i % 251)).ToArray();
    var path = Path.Combine(testRoot, "mid-cancel.bin");
    var file = new TransferFile(path, ["https://test.invalid/file"], data.Length, Convert.ToHexString(SHA1.HashData(data)));
    using var network = new DownloadClient(new(), new FakeHandler(request =>
    {
        var offset = request.Headers.Range?.Ranges.Single().From ?? 0;
        var response = new HttpResponseMessage(offset > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(data[(int)offset..]) };
        if (offset > 0) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, data.Length - 1, data.Length);
        return response;
    }));
    using var cancel = new CancellationTokenSource();
    await ThrowsAsync<OperationCanceledException>(() => network.DownloadAsync(file, cancel.Token, _ => cancel.Cancel()));
    Check(!File.Exists(path) && new FileInfo(path + ".part").Length > 0, "partial retained");
    await network.DownloadAsync(file, default);
    Check(File.ReadAllBytes(path).SequenceEqual(data), "resumed correctly");
}));
tests.Add(("Forge 镜像模式使用专用列表并保留旧版本分支", async () =>
{
    using var network = new DownloadClient(new() { Source = DownloadSource.Mirror }, new FakeHandler(request =>
    {
        Check(request.RequestUri!.AbsolutePath == "/forge/minecraft/1.7.10", "dedicated Forge API");
        return new(HttpStatusCode.OK) { Content = new StringContent("""[{"version":"10.13.4.1614","branch":"1.7.10","files":[{"category":"installer","format":"jar"}]}]""") };
    }));
    var list = await new MinecraftInstaller(new() { Source = DownloadSource.Mirror }, network).GetLoaderVersionsAsync(LoaderKind.Forge, "1.7.10", default);
    Check(list.Single().Version == "10.13.4.1614-1.7.10", "legacy branch preserved");
}));
tests.Add(("识别 Modrinth 版本与环境标记", async () =>
{
    var path = await ArchiveAsync("modrinth.mrpack", new() { ["modrinth.index.json"] = """{"formatVersion":1,"game":"minecraft","name":"测试整合包","dependencies":{"minecraft":"1.20.1","fabric-loader":"0.16.0"},"files":[{"env":{"client":"required"}},{"env":{"client":"unsupported"}},{"env":{"client":"optional"}}]}""" });
    var pack = await ModpackInstaller.InspectAsync(path, default);
    Check(pack.Loader == LoaderKind.Fabric && pack.FileCount == 2 && pack.OptionalCount == 1, "mrpack info");
}));
tests.Add(("识别嵌套 CurseForge ZIP", async () =>
{
    var path = await ArchiveAsync("curse.zip", new() { ["pack/manifest.json"] = """{"manifestType":"minecraftModpack","manifestVersion":1,"name":"CF","minecraft":{"version":"1.20.1","modLoaders":[{"id":"forge-47.3.0","primary":true}]},"files":[],"overrides":"overrides"}""" });
    var pack = await ModpackInstaller.InspectAsync(path, default);
    Check(pack.Prefix == "pack/" && pack.LoaderVersion == "47.3.0", "CF info");
}));
tests.Add(("识别 MultiMC 和 MCBBS 整合包", async () =>
{
    var mmc = await ArchiveAsync("mmc.zip", new() { ["mmc-pack.json"] = """{"components":[{"uid":"net.minecraft","version":"1.21.1"},{"uid":"net.neoforged","version":"21.1.1"}]}""", ["instance.cfg"] = "name=多实例测试\n", [".minecraft/config/test.txt"] = "ok" });
    var pack = await ModpackInstaller.InspectAsync(mmc, default);
    Check(pack.Name == "多实例测试" && pack.Loader == LoaderKind.NeoForge && pack.Overrides == ".minecraft", "MultiMC");
    var mcbbs = await ArchiveAsync("mcbbs.zip", new() { ["mcbbs.packmeta"] = """{"name":"MCBBS","addons":[{"id":"game","version":"1.20.1"},{"id":"fabric","version":"0.16.0"}]}""" });
    Check((await ModpackInstaller.InspectAsync(mcbbs, default)).Loader == LoaderKind.Fabric, "MCBBS");
}));
tests.Add(("ZIP 路径穿越在预览阶段拒绝", async () =>
{
    var path = await ArchiveAsync("evil.zip", new() { ["../escape.txt"] = "bad" });
    await ThrowsAsync<InvalidDataException>(() => ModpackInstaller.InspectAsync(path, default));
}));
tests.Add(("不支持的 Quilt 不会静默当原版", async () =>
{
    var path = await ArchiveAsync("quilt.mrpack", new() { ["modrinth.index.json"] = """{"formatVersion":1,"game":"minecraft","dependencies":{"minecraft":"1.20.1","quilt-loader":"1.0"},"files":[]}""" });
    await ThrowsAsync<NotSupportedException>(() => ModpackInstaller.InspectAsync(path, default));
}));
tests.Add(("客户端覆盖层优先，跳过服务端文件", async () =>
{
    var path = await ArchiveAsync("overrides.mrpack", new() { ["modrinth.index.json"] = """{"formatVersion":1,"game":"minecraft","dependencies":{"minecraft":"1.20.1"},"files":[]}""", ["overrides/config/a.txt"] = "base", ["client-overrides/config/a.txt"] = "client", ["server-overrides/server.txt"] = "no" });
    var pack = await ModpackInstaller.InspectAsync(path, default);
    var target = Path.Combine(testRoot, "layers");
    using var network = new DownloadClient(new());
    await new ModpackInstaller(new(), network).ApplyAsync(pack, target, null, default);
    Check(File.ReadAllText(Path.Combine(target, "config/a.txt")) == "client" && !File.Exists(Path.Combine(target, "server.txt")), "client layers");
}));
tests.Add(("原生 ZIP 保留 NeoForge beta 版本与根配置", async () =>
{
    var path = await ArchiveAsync("raw.zip", new() { [".minecraft/versions/custom/custom.json"] = """{"id":"custom","inheritsFrom":"1.20.2","libraries":[{"name":"net.neoforged:neoforge:20.2.1-beta"}]}""", [".minecraft/options.json"] = "{\"test\":true}", [".minecraft/versions/custom/config/sample.json"] = "{}" });
    var pack = await ModpackInstaller.InspectAsync(path, default);
    Check(pack.Loader == LoaderKind.NeoForge && pack.LoaderVersion == "20.2.1-beta", "beta preserved");
    using var network = new DownloadClient(new());
    var target = Path.Combine(testRoot, "raw-result");
    await new ModpackInstaller(new(), network).ApplyAsync(pack, target, null, default);
    Check(File.Exists(Path.Combine(target, "options.json")) && File.Exists(Path.Combine(target, "config/sample.json")), "root and isolated config imported");
    Check(!File.Exists(Path.Combine(target, "custom.json")), "old profile omitted");
}));
tests.Add(("完整原版流程：本体、库、资源、原子提交与防覆盖", async () =>
{
    var options = new AppSettings { GameDirectory = Path.Combine(testRoot, "e2e") };
    var content = new Dictionary<string, byte[]>();
    var assetHash = Convert.ToHexString(SHA1.HashData(payload)).ToLowerInvariant();
    var assetIndex = Encoding.UTF8.GetBytes(new JsonObject { ["objects"] = new JsonObject { ["minecraft/sounds/test.ogg"] = new JsonObject { ["hash"] = assetHash, ["size"] = payload.Length } } }.ToJsonString());
    var version = new JsonObject { ["id"] = "1.20.1", ["downloads"] = new JsonObject { ["client"] = Artifact("https://test.invalid/client", payload) }, ["assetIndex"] = Artifact("https://test.invalid/index", assetIndex), ["libraries"] = new JsonArray(new JsonObject { ["name"] = "test:lib:1", ["downloads"] = new JsonObject { ["artifact"] = Artifact("https://test.invalid/library", payload) } }) };
    version["assetIndex"]!["id"] = "test"; version["libraries"]![0]!["downloads"]!["artifact"]!["path"] = "test/lib/1/lib-1.jar";
    var versionBytes = Encoding.UTF8.GetBytes(version.ToJsonString());
    var manifest = new JsonObject { ["versions"] = new JsonArray(new JsonObject { ["id"] = "1.20.1", ["type"] = "release", ["url"] = "https://test.invalid/version", ["sha1"] = Convert.ToHexString(SHA1.HashData(versionBytes)), ["releaseTime"] = "2023-06-12T00:00:00Z" }) };
    content[MinecraftInstaller.ManifestUrl] = Encoding.UTF8.GetBytes(manifest.ToJsonString());
    content["https://test.invalid/version"] = versionBytes; content["https://test.invalid/client"] = payload;
    content["https://test.invalid/index"] = assetIndex; content["https://test.invalid/library"] = payload;
    content[$"https://resources.download.minecraft.net/{assetHash[..2]}/{assetHash}"] = payload;
    using var network = new DownloadClient(options, new FakeHandler(req => content.TryGetValue(req.RequestUri!.AbsoluteUri, out var data) ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) } : new(HttpStatusCode.NotFound)));
    var installer = new MinecraftInstaller(options, network);
    var result = await installer.InstallAsync(new("1.20.1", "测试原版"), null, default);
    Check(File.Exists(Path.Combine(result, "测试原版.jar")), "client installed");
    Check(File.Exists(Path.Combine(result, "测试原版.json")), "profile committed");
    Check(File.Exists(Path.Combine(options.GameDirectory, "assets", "objects", assetHash[..2], assetHash)), "asset installed");
    Check(File.ReadAllText(Path.Combine(result, "PCL", "Setup.ini")).Contains("VersionArgumentIndie:1"), "PCL isolation");
    await ThrowsAsync<IOException>(() => installer.InstallAsync(new("1.20.1", "测试原版"), null, default));
    var archive = await ArchiveAsync("e2e-icon.mrpack", new() { ["modrinth.index.json"] = """{"formatVersion":1,"game":"minecraft","name":"图标整合包","dependencies":{"minecraft":"1.20.1"},"files":[]}""" });
    var pack = await ModpackInstaller.InspectAsync(archive, default);
    pack.IconUrl = "https://cdn.modrinth.com/example/icon.png";
    var packed = await installer.InstallAsync(new("1.20.1", "图标整合包"), null, default, pack);
    var saved = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(packed, "blockdock-instance.json")))!;
    Check(saved.Str("iconUrl") == pack.IconUrl, "提交实例时应保存整合包图标");
}));

ModpackCatalogTests.AddTo(tests, testRoot);
InstalledLibraryTests.AddTo(tests, testRoot);
JavaDiscoveryTests.AddTo(tests, testRoot);
int failed = 0;
foreach (var (name, test) in tests)
{
    try { await test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + "\n" + ex); }
}
Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed. Fixtures: {testRoot}");
Environment.ExitCode = failed == 0 ? 0 : 1;

async Task<string> ArchiveAsync(string name, Dictionary<string, string> entries)
{
    var path = Path.Combine(testRoot, name);
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var (entryName, text) in entries) { await using var writer = new StreamWriter(zip.CreateEntry(entryName).Open()); await writer.WriteAsync(text); }
    return path;
}
static JsonObject Artifact(string url, byte[] data) => new() { ["url"] = url, ["size"] = data.Length, ["sha1"] = Convert.ToHexString(SHA1.HashData(data)) };
static void Check(bool value, string message) { if (!value) throw new Exception("Assertion failed: " + message); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handle(request));
}
sealed class ThrottledProgress : IProgress<InstallProgress>
{
    private long previous;
    private string stage = "";
    private readonly object gate = new();
    public void Report(InstallProgress p)
    {
        lock (gate)
        {
            if (Environment.TickCount64 - previous < 3000 && stage == p.Stage) return;
            previous = Environment.TickCount64; stage = p.Stage;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {p.Stage} {p.Completed}/{p.Total} {p.Detail} ({p.Bytes / 1048576:F1} MiB)");
        }
    }
}
