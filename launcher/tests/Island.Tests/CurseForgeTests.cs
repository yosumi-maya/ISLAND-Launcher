using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BlockDock.Core;
using Island.Core;

internal static class CurseForgeTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request));
        }
    }
    private sealed class Progress : IProgress<InstallProgress> { public void Report(InstallProgress progress) { } }

    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        const string key = "$2a$10$fixture/with.dollars";
        var directory = Path.Combine(root, "curseforge");
        var state = new LauncherState { CurseForgeApiKey = key };
        check(!System.Text.Json.JsonSerializer.Serialize(state).Contains(key) && state.DownloadSettings().CurseForgeApiKey == key,
            "API keys reach download snapshots without entering launcher settings JSON");
        var payload = Encoding.UTF8.GetBytes("CurseForge owned fixture");
        var hash = Convert.ToHexString(SHA1.HashData(payload));
        JsonObject Project(int id) => new()
        {
            ["id"] = id, ["gameId"] = 432, ["classId"] = 6, ["name"] = "Fixture " + id, ["slug"] = "fixture-" + id,
            ["allowModDistribution"] = true, ["latestFilesIndexes"] = new JsonArray(), ["links"] = new JsonObject()
        };
        JsonObject ReleaseFile(int project) => new()
        {
            ["id"] = project * 10, ["modId"] = project, ["fileName"] = "fixture-" + project + ".jar",
            ["displayName"] = "Fixture", ["fileLength"] = payload.Length, ["releaseType"] = 1,
            ["isAvailable"] = true, ["downloadUrl"] = "https://edge.forgecdn.net/files/" + project + ".jar",
            ["gameVersions"] = new JsonArray("1.20.1", "Forge"),
            ["hashes"] = new JsonArray(new JsonObject { ["algo"] = 1, ["value"] = hash }),
            ["dependencies"] = project == 1 ? new JsonArray(new JsonObject { ["modId"] = 2, ["relationType"] = 3 }) : new JsonArray()
        };
        var settings = new AppSettings { GameDirectory = Path.Combine(directory, "game"), CurseForgeApiKey = key };
        var requests = new List<Uri>();
        var credentialPreserved = true;
        var blocked = false;
        var searches = 0;
        var source = new ContentDownloads(options => new DownloadClient(options, new Handler(request =>
        {
            var uri = request.RequestUri!; requests.Add(uri);
            credentialPreserved &= request.Headers.TryGetValues("x-api-key", out var values) && values.Single() == key
                && request.Headers.UserAgent.Count > 0;
            if (uri.Host == "edge.forgecdn.net") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
            if (uri.AbsolutePath.EndsWith("/search"))
            {
                searches++;
                if (blocked) return new(HttpStatusCode.Forbidden);
                return Json(new JsonObject { ["data"] = new JsonArray(Project(1)), ["pagination"] = new JsonObject { ["totalCount"] = 10001 } });
            }
            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var id = int.Parse(parts[2]);
            if (parts.Length == 3) return Json(new JsonObject { ["data"] = Project(id) });
            if (parts.Length == 4) return Json(new JsonObject
            {
                ["data"] = new JsonArray(ReleaseFile(id)),
                ["pagination"] = new JsonObject { ["totalCount"] = 1, ["resultCount"] = 1 }
            });
            return Json(new JsonObject { ["data"] = ReleaseFile(id) });
        })));
        var filter = new ContentFilter(ContentKind.Mod, "fixture", "1.20.1", "forge") { Platform = PackPlatform.CurseForge };
        var unfilteredUrl = ModpackCatalog.BuildCurseForgeSearchUrl(new("jei", Platform: PackPlatform.CurseForge), 0, 6);
        check(!unfilteredUrl.Contains("categoryId=", StringComparison.Ordinal)
            && !unfilteredUrl.Contains("gameVersion=", StringComparison.Ordinal)
            && !unfilteredUrl.Contains("modLoaderType=", StringComparison.Ordinal)
            && unfilteredUrl.Contains("classId=6", StringComparison.Ordinal),
            "All categories and versions omit optional filters instead of sending categoryId=0");
        var forgeUrl = ModpackCatalog.BuildCurseForgeSearchUrl(new("jei", "1.20.1", PackPlatform.CurseForge, LoaderKind.Forge), 0, 6);
        check(forgeUrl.Contains("gameVersion=1.20.1", StringComparison.Ordinal)
            && forgeUrl.Contains("modLoaderType=1", StringComparison.Ordinal)
            && !forgeUrl.Contains("categoryId=", StringComparison.Ordinal),
            "A Forge version filter retains the correct class and omits an unselected category");
        var page = await source.SearchAsync(settings, filter, 499, default);
        check(page.Page == 499 && !page.HasNext && requests.Last().Query.Contains("index=9980")
            && requests.Last().Query.Contains("pageSize=20") && requests.Last().Query.Contains("classId=6"),
            "CurseForge search uses 20-item pages and stops at the 10000-result boundary");
        blocked = true;
        var denied = await source.SearchAsync(settings, filter, 0, default);
        check(denied.Notices.Single().Contains("403") && searches == 2, "A forbidden search is reported without automatic retries");
        var direct = await source.SearchAsync(settings, filter with { Query = "1" }, 0, default);
        check(direct.Projects.Single().Id == "1" && searches == 2, "A project ID remains usable when keyword search is forbidden");
        var project = direct.Projects.Single();
        var versions = await source.GetVersionsAsync(settings, project, filter, default);
        var version = versions.Single();
        var instance = Path.Combine(settings.GameDirectory, "versions", "Sample");
        Directory.CreateDirectory(instance);
        File.WriteAllText(Path.Combine(instance, "Sample.json"), """{"id":"Sample","inheritsFrom":"1.20.1"}""");
        File.WriteAllText(Path.Combine(instance, "blockdock-instance.json"), """{"minecraft":"1.20.1","loader":"Forge"}""");
        var target = new ContentTarget(settings.GameDirectory, "Sample", "Sample", "1.20.1", "Forge", instance, VersionIsolation.Enabled);
        var plan = await source.PrepareAsync(settings, project, version, filter, target, default);
        check(plan.Files.Count == 2 && plan.Files.Single(file => file.IsDependency).ProjectId == "curseforge:2",
            "CurseForge releases resolve required dependencies into the existing install confirmation plan");
        var mods = Path.Combine(instance, "mods"); Directory.CreateDirectory(mods);
        var renamed = Path.Combine(mods, "[前置]renamed.jar"); File.WriteAllBytes(renamed, payload);
        var matches = await ContentInstallationInspector.InspectAsync(plan, default);
        check(matches.Any(match => match.ProjectId == "curseforge:2" && match.Confirmed && match.Path == renamed),
            "SHA-1 identifies renamed CurseForge dependencies including Chinese filename prefixes");
        File.Delete(renamed);
        await source.InstallAsync(settings, plan, new Progress(), default);
        check(plan.TargetFiles.All(path => File.ReadAllBytes(path).SequenceEqual(payload)) && credentialPreserved,
            "CurseForge API and CDN requests retain the exact key and installed files pass SHA-1 verification");

        await CheckPackDownloadFallbackAsync(directory, payload, hash, key, check);

        var redirectHeadersSafe = true;
        using var redirect = new DownloadClient(settings, new Handler(request =>
        {
            if (request.RequestUri!.Host == "edge.forgecdn.net")
            {
                redirectHeadersSafe &= request.Headers.Contains("x-api-key");
                return new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://fixture.invalid/file.jar") } };
            }
            redirectHeadersSafe &= !request.Headers.Contains("x-api-key");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        }));
        await redirect.DownloadAsync(new(Path.Combine(directory, "redirect.jar"), ["https://edge.forgecdn.net/file.jar"], payload.Length, hash), default);
        check(redirectHeadersSafe, "Download redirects never forward the CurseForge key to unrelated hosts");
        var limitedCalls = 0;
        using var limited = new DownloadClient(settings, new Handler(_ =>
        {
            limitedCalls++;
            if (limitedCalls > 1) return Json(new JsonObject());
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero); return response;
        }));
        await limited.GetJsonAsync("https://api.curseforge.com/v1/games/432", default, key);
        check(limitedCalls == 2, "Rate-limited requests honor Retry-After with a bounded retry");
        var providerUnavailable = false;
        var merged = new ContentDownloads(options => new DownloadClient(options, new Handler(request =>
        {
            if (request.RequestUri!.Host == "api.modrinth.com")
                return Json(new JsonObject
                {
                    ["hits"] = new JsonArray(new JsonObject { ["project_id"] = "modrinth-fixture", ["title"] = "Modrinth fixture", ["downloads"] = 100 }),
                    ["total_hits"] = 20000
                });
            return providerUnavailable ? new(HttpStatusCode.Forbidden)
                : Json(new JsonObject { ["data"] = new JsonArray(Project(3)), ["pagination"] = new JsonObject { ["totalCount"] = 21 } });
        })));
        var allSources = filter with { Platform = PackPlatform.All };
        var mixed = await merged.SearchAsync(settings, allSources, 0, default);
        check(mixed.Projects.Select(item => item.Platform).Distinct().Count() == 2 && mixed.Total == 20021 && mixed.HasNext,
            "All sources merges Modrinth and CurseForge resources and combines pagination");
        providerUnavailable = true;
        var partial = await merged.SearchAsync(settings, allSources, 0, default);
        check(partial.Projects.Single().Platform == PackPlatform.Modrinth && partial.Notices.Single().Contains("403"),
            "All sources preserves usable results when one provider fails");
        var last = await merged.SearchAsync(settings, allSources, 499, default);
        check(!last.HasNext, "All sources stops exposing next pages at the shared catalog boundary");

    }

    private static async Task CheckPackDownloadFallbackAsync(string directory, byte[] payload, string hash, string key, Action<bool, string> check)
    {
        const string filename = "immersive fixes+1.8.0.jar";
        const string fileApiPath = "/v1/mods/files";
        const string cdnPath = "/files/9181/462/immersive%20fixes%2B1.8.0.jar";
        var archive = Path.Combine(directory, "manifest-fallback.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
            writer.Write("""{"manifestType":"minecraftModpack","manifestVersion":1,"name":"Fixture","minecraft":{"version":"1.20.1"},"files":[{"projectID":1326199,"fileID":9181462,"required":true},{"projectID":1001614,"fileID":5922047,"required":true}]}""");
        var pack = await ModpackInstaller.InspectAsync(archive, default);
        var settings = new AppSettings { CurseForgeApiKey = key, Source = DownloadSource.Official, ParallelDownloads = 1 };
        var requests = new List<(string Host, string Path)>();
        var metadataStatus = HttpStatusCode.OK;
        using var network = new DownloadClient(settings, new Handler(request =>
        {
            var uri = request.RequestUri!;
            requests.Add((uri.Host, uri.AbsolutePath));
            check(request.Headers.GetValues("x-api-key").Single() == key, "CurseForge pack CDN requests keep the configured API credential");
            if (uri.AbsolutePath == fileApiPath)
            {
                var body = JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                check(request.Method == HttpMethod.Post && body?["fileIds"]?.AsArray().Select(id => id!.GetValue<long>()).SequenceEqual([9181462L, 5922047L]) == true,
                    "One batch metadata request sends both file IDs from the manifest");
            }
            return (uri.Host, uri.AbsolutePath) switch
            {
                ("api.curseforge.com", fileApiPath) when metadataStatus == HttpStatusCode.OK => Json(new JsonObject
                {
                    ["data"] = new JsonArray(new JsonObject
                    {
                        ["modId"] = 1326199, ["id"] = 9181462, ["isAvailable"] = true, ["fileName"] = filename,
                        ["fileLength"] = payload.Length, ["downloadUrl"] = null,
                        ["hashes"] = new JsonArray(new JsonObject { ["algo"] = 1, ["value"] = hash })
                    },
                    new JsonObject
                    {
                        ["modId"] = 1001614, ["id"] = 5922047, ["isAvailable"] = false, ["fileName"] = "lionfishapi-2.4-Fix.jar",
                        ["fileLength"] = payload.Length, ["downloadUrl"] = "https://mediafilez.forgecdn.net/files/5922/47/lionfishapi-2.4-Fix.jar",
                        ["hashes"] = new JsonArray(new JsonObject { ["algo"] = 1, ["value"] = hash })
                    })
                }),
                ("api.curseforge.com", fileApiPath) => new(metadataStatus),
                ("edge.forgecdn.net", cdnPath) => new(HttpStatusCode.ServiceUnavailable),
                ("mediafilez.forgecdn.net", cdnPath or "/files/5922/47/lionfishapi-2.4-Fix.jar") => new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) },
                _ => throw new InvalidOperationException("Unexpected CurseForge request: " + uri)
            };
        }));
        var installer = new ModpackInstaller(settings, network);
        var instance = Path.Combine(directory, "manifest-fallback");
        await installer.ApplyAsync(pack, instance, null, default);
        check(requests.SequenceEqual(new[] { ("api.curseforge.com", fileApiPath),
                ("edge.forgecdn.net", cdnPath), ("mediafilez.forgecdn.net", cdnPath),
                ("mediafilez.forgecdn.net", "/files/5922/47/lionfishapi-2.4-Fix.jar") })
            && File.ReadAllBytes(Path.Combine(instance, "mods", filename)).SequenceEqual(payload)
            && File.ReadAllBytes(Path.Combine(instance, "mods", "lionfishapi-2.4-Fix.jar")).SequenceEqual(payload),
            "CurseForge manifests use CDN fallbacks for missing links and honor an explicit link for an archived file");

        metadataStatus = HttpStatusCode.Unauthorized;
        requests.Clear();
        var unauthorized = false;
        try { await installer.ApplyAsync(pack, Path.Combine(directory, "unauthorized"), null, default); }
        catch (CurseForgeRequestException ex) when (ex.Status == HttpStatusCode.Unauthorized) { unauthorized = true; }
        check(unauthorized && requests.Count == 1, "A rejected CurseForge key is reported without making CDN requests");
    }

    private static HttpResponseMessage Json(JsonObject value) => new(HttpStatusCode.OK) { Content = new StringContent(value.ToJsonString()) };
}
