using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BlockDock.Core;
using Island.Core;

internal static class ContentDownloadTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
    private sealed class Progress(Action<InstallProgress> action) : IProgress<InstallProgress> { public void Report(InstallProgress value) => action(value); }
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        var game = Path.Combine(root, "content-game");
        ContentTarget Target(string id, string loader, VersionIsolation isolation = VersionIsolation.Enabled)
        {
            var path = Path.Combine(game, "versions", id); Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, id + ".json"), "{\"id\":\"" + id + "\",\"inheritsFrom\":\"1.20.1\"}");
            File.WriteAllText(Path.Combine(path, "blockdock-instance.json"), "{\"minecraft\":\"1.20.1\",\"loader\":\"" + loader + "\"}");
            return new(game, id, id, "1.20.1", loader, GameDirectories.Resolve(game, id, isolation).Directory, isolation);
        }
        var target = Target("Fabric content", "Fabric"); var shared = Target("Shared content", "Vanilla", VersionIsolation.Disabled);
        var settings = new AppSettings { GameDirectory = game };
        var payloads = new Dictionary<string, byte[]>(); var versions = new Dictionary<string, JsonObject>(); var projects = new Dictionary<string, string>(); var downloads = 0;
        CatalogProject Project(string id, ContentKind kind) => new(id, id, "fixture", PackPlatform.Modrinth, 1, DateTimeOffset.Now, DateTimeOffset.Now, ["1.20.1"], [], "https://modrinth.com/" + ContentDownloads.Slug(kind) + "/" + id);
        CatalogVersion Add(string id, string versionId, ContentKind kind, string? filename = null, string loader = "fabric", string minecraft = "1.20.1", JsonArray? dependencies = null)
        {
            projects[id] = ContentDownloads.Slug(kind); var bytes = Encoding.UTF8.GetBytes("owned fixture " + versionId); payloads[versionId] = bytes;
            var data = new JsonObject { ["id"] = versionId, ["project_id"] = id, ["name"] = versionId, ["game_versions"] = new JsonArray(minecraft), ["loaders"] = new JsonArray(loader), ["version_type"] = "release", ["date_published"] = "2026-10-25T00:00:00Z", ["dependencies"] = dependencies ?? [],
                ["files"] = new JsonArray(new JsonObject { ["primary"] = true, ["filename"] = filename ?? versionId + (kind == ContentKind.Mod ? ".jar" : ".zip"), ["url"] = "https://fixture.invalid/" + versionId, ["size"] = bytes.Length,
                    ["hashes"] = new JsonObject { ["sha1"] = Convert.ToHexString(SHA1.HashData(bytes)), ["sha512"] = Convert.ToHexString(SHA512.HashData(bytes)) } }) };
            versions[versionId] = data; return new(versionId, versionId, [minecraft], [loader], DateTimeOffset.Now, "正式版", data);
        }
        JsonObject Dependency(string id, string? version = null, string type = "required") => new() { ["project_id"] = id, ["version_id"] = version, ["dependency_type"] = type };
        var dependency = Add("fabric-api", "api-v1", ContentKind.Mod);
        var main = Add("mod", "mod-v1", ContentKind.Mod, dependencies: new JsonArray(Dependency("fabric-api"), Dependency("optional", type: "optional")));
        var requests = new List<string>(); var invalidProviderRequest = false;
        var service = new ContentDownloads(options => new DownloadClient(options, new Handler(request =>
        {
            var uri = request.RequestUri!; requests.Add(uri.AbsoluteUri);
            invalidProviderRequest |= request.Headers.Contains("x-api-key") || uri.Host.Contains("curseforge");
            if (uri.Host == "fixture.invalid") { downloads++; return new(HttpStatusCode.OK) { Content = new ByteArrayContent(payloads[uri.AbsolutePath.TrimStart('/')]) }; }
            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            JsonNode result;
            if (parts.Last() == "search") result = new JsonObject { ["total_hits"] = 21, ["hits"] = new JsonArray(new JsonObject { ["project_id"] = "mod", ["title"] = "Fixture Mod", ["slug"] = "mod", ["categories"] = new JsonArray("fabric", "technology"), ["versions"] = new JsonArray("1.20.1") }) };
            else if (parts[1] == "version") result = versions[parts[2]];
            else if (parts.Last() == "version") result = new JsonArray(versions.Values.Where(item => item["project_id"]!.GetValue<string>() == parts[2]).Select(item => item.DeepClone()).ToArray());
            else result = new JsonObject { ["id"] = parts[2], ["title"] = parts[2], ["project_type"] = projects[parts[2]], ["client_side"] = "required" };
            return new(HttpStatusCode.OK) { Content = new StringContent(result.ToJsonString()) };
        })));
        async Task Reject(Func<Task> action, string label)
        {
            try { await action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException) { check(true, label); return; }
            throw new Exception("Expected rejection: " + label);
        }
        foreach (var kind in Enum.GetValues<ContentKind>())
        {
            var filter = new ContentFilter(kind, "test & content", "1.20.1", kind == ContentKind.Mod ? "fabric" : "", PackSort.Updated);
            var page = await service.SearchAsync(settings, filter, 1, default);
            var decoded = Uri.UnescapeDataString(requests.Last());
            check(page.Total == 21 && !page.HasNext && page.Projects[0].Versions.SequenceEqual(new[] { "1.20.1" }) && decoded.Contains("project_type:" + ContentDownloads.Slug(kind)) && decoded.Contains("offset=20") && decoded.Contains("versions:1.20.1") && decoded.Contains("index=updated"), "Content type, version, pagination and sorting use AND search facets: " + kind);
        }
        var modFilter = new ContentFilter(ContentKind.Mod, Minecraft: "1.20.1", Loader: "fabric");
        var releases = await service.GetVersionsAsync(settings, Project("mod", ContentKind.Mod), modFilter, default);
        check(releases.Single().Id == main.Id, "Content versions preserve the instance Minecraft and loader filters");
        var plan = await service.PrepareAsync(settings, Project("mod", ContentKind.Mod), main, modFilter, target, default);
        check(plan.Files.Count == 2 && plan.Files[0].ProjectId == "fabric-api" && !requests.Any(url => url.Contains("optional")), "Required dependencies resolve before the main mod and optional dependencies remain untouched");
        await service.InstallAsync(settings, plan, new Progress(_ => { }), default);
        check(plan.TargetFiles.All(File.Exists) && plan.TargetFiles.All(path => path.StartsWith(Path.Combine(target.RuntimeDirectory, "mods"))) && File.Exists(Path.Combine(target.RuntimeDirectory, ".island", "content.json")), "Content installation verifies and commits required JARs into the isolated instance");
        var before = downloads; await service.InstallAsync(settings, plan, new Progress(_ => { }), default);
        check(downloads == before, "Reinstalling identical content reuses valid files without downloading again");
        var newer = Add("mod", "mod-v2", ContentKind.Mod);
        await Reject(() => service.PrepareAsync(settings, Project("mod", ContentKind.Mod), newer, modFilter, target, default), "A managed older project version blocks duplicate-mod installation");
        var shader = Add("shader", "shader-v1", ContentKind.Shader, loader: "iris");
        var shaderPlan = await service.PrepareAsync(settings, Project("shader", ContentKind.Shader), shader, new(ContentKind.Shader, Loader: "iris"), target, default);
        await service.InstallAsync(settings, shaderPlan, new Progress(_ => { }), default);
        check(File.Exists(Path.Combine(target.RuntimeDirectory, "shaderpacks", "shader-v1.zip")), "Shader ZIPs stay packed in shaderpacks");
        var resource = Add("resources", "resources-v1", ContentKind.ResourcePack, loader: "minecraft");
        var resourcePlan = await service.PrepareAsync(settings, Project("resources", ContentKind.ResourcePack), resource, new(ContentKind.ResourcePack), shared, default);
        await service.InstallAsync(settings, resourcePlan, new Progress(_ => { }), default);
        check(File.Exists(Path.Combine(game, "resourcepacks", "resources-v1.zip")) && !File.Exists(Path.Combine(target.RuntimeDirectory, "resourcepacks", "resources-v1.zip")), "Resource packs respect shared directory resolution without touching another isolated instance");
        await Reject(() => service.PrepareAsync(settings, Project("mod", ContentKind.Mod), main, modFilter, shared, default), "Vanilla instances reject mod installation before resource requests");
        var wrong = Add("wrong", "wrong-v1", ContentKind.Mod, loader: "forge");
        await Reject(() => service.PrepareAsync(settings, Project("wrong", ContentKind.Mod), wrong, modFilter, target, default), "A mismatched mod loader is rejected before writing files");
        var traversal = Add("unsafe", "unsafe-v1", ContentKind.ResourcePack, filename: "../outside.zip", loader: "minecraft");
        await Reject(() => service.PrepareAsync(settings, Project("unsafe", ContentKind.ResourcePack), traversal, new(ContentKind.ResourcePack), target, default), "Remote resource filenames cannot escape their content folder");
        var hashless = Add("hashless", "hashless-v1", ContentKind.Mod); versions[hashless.Id]["files"]![0]!["hashes"]!["sha512"] = "bad";
        await Reject(() => service.PrepareAsync(settings, Project("hashless", ContentKind.Mod), hashless, modFilter, target, default), "Missing or malformed resource hashes stop installation planning");
        var conflict = Add("conflict", "conflict-v1", ContentKind.Mod);
        var conflictPlan = await service.PrepareAsync(settings, Project("conflict", ContentKind.Mod), conflict, modFilter, target, default);
        File.WriteAllText(conflictPlan.TargetFiles[0], "user file");
        await Reject(() => service.InstallAsync(settings, conflictPlan, new Progress(_ => { }), default), "Existing differing files are never overwritten");
        check(File.ReadAllText(conflictPlan.TargetFiles[0]) == "user file", "Rejected resource replacement preserves the user's original file");
        var bad = Add("bad", "bad-v1", ContentKind.Mod, dependencies: new JsonArray(Dependency("fabric-api", "api-v1")));
        var badPlan = await service.PrepareAsync(settings, Project("bad", ContentKind.Mod), bad, modFilter, target, default);
        payloads[bad.Id] = Encoding.UTF8.GetBytes("corrupted download");
        await Reject(() => service.InstallAsync(settings, badPlan, new Progress(_ => { }), default), "Corrupt resource bytes fail hash validation");
        check(!File.Exists(badPlan.TargetFiles.Last()) && File.Exists(plan.TargetFiles[0]), "A failed resource download leaves the main file absent and preserves existing dependencies");
        var cancel = Add("cancel", "cancel-v1", ContentKind.Mod);
        var cancelPlan = await service.PrepareAsync(settings, Project("cancel", ContentKind.Mod), cancel, modFilter, target, default);
        using var cancellation = new CancellationTokenSource();
        try { await service.InstallAsync(settings, cancelPlan, new Progress(value => { if (value.Stage == "校验资源") cancellation.Cancel(); }), cancellation.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { check(!File.Exists(cancelPlan.TargetFiles[0]), "Canceling resource download before commit does not install a partial file"); }
        var cachedDownloads = downloads;
        await service.InstallAsync(settings, cancelPlan, new Progress(_ => { }), default);
        check(File.Exists(cancelPlan.TargetFiles[0]) && downloads == cachedDownloads, "Resuming a resource download reuses its validated download cache");
        var incompatible = Add("incompatible", "incompatible-v1", ContentKind.Mod, dependencies: new JsonArray(Dependency("fabric-api", type: "incompatible")));
        await Reject(() => service.PrepareAsync(settings, Project("incompatible", ContentKind.Mod), incompatible, modFilter, target, default), "Declared incompatibilities with managed installed projects block installation");
        var specific = Add("specific", "specific-v1", ContentKind.Mod, dependencies: new JsonArray(Dependency("fabric-api", "api-other-version", "incompatible")));
        var specificPlan = await service.PrepareAsync(settings, Project("specific", ContentKind.Mod), specific, modFilter, target, default);
        check(specificPlan.Files.Count == 1, "An incompatible pinned version does not block a different installed version of the same project");
        var pendingA = Add("pending-a", "pending-a-v1", ContentKind.Mod, dependencies: new JsonArray(Dependency("pending-b", type: "incompatible")));
        var pendingB = Add("pending-b", "pending-b-v1", ContentKind.Mod);
        var planA = await service.PrepareAsync(settings, Project("pending-a", ContentKind.Mod), pendingA, modFilter, target, default);
        var planB = await service.PrepareAsync(settings, Project("pending-b", ContentKind.Mod), pendingB, modFilter, target, default);
        await service.InstallAsync(settings, planB, new Progress(_ => { }), default);
        await Reject(() => service.InstallAsync(settings, planA, new Progress(_ => { }), default), "Queued resource installs recheck incompatible projects after earlier jobs complete");
        File.Delete(planB.TargetFiles.Single());
        await service.InstallAsync(settings, planA, new Progress(_ => { }), default);
        await Reject(() => service.PrepareAsync(settings, Project("pending-b", ContentKind.Mod), pendingB, modFilter, target, default), "Installed projects retain their incompatibility declarations for later downloads");
        var corruptPlan = cancelPlan with { Files = [cancelPlan.Files[0] with { Sha512 = "../outside" }] };
        await Reject(() => service.InstallAsync(settings, corruptPlan, new Progress(_ => { }), default), "Installation independently validates resource hashes before constructing cache paths");
        await Reject(() => service.InstallAsync(settings, resourcePlan with { Target = shared with { RuntimeDirectory = target.RuntimeDirectory } }, new Progress(_ => { }), default), "A stale or substituted runtime directory is rejected before downloading");
        check(!invalidProviderRequest, "Content downloads send no CurseForge requests or credentials");
    }
}
