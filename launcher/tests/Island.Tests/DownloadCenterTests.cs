using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BlockDock.Core;
using Island.Core;

internal static class DownloadCenterTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
    private static async Task Reject(Func<Task> action, Action<bool, string> check, string label)
    {
        try { await action(); }
        catch (InvalidOperationException ex) when (ex.Message == DownloadAvailability.CurseForgeNotice || ex.Message == ModpackCatalog.CurseForgeKeyNotice) { check(true, label); return; }
        throw new Exception(label);
    }
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        var directory = Path.Combine(root, "integrated-downloader"); Directory.CreateDirectory(directory);
        var settings = new AppSettings { GameDirectory = directory, JavaPath = "fixture-java", ParallelDownloads = 16, Source = DownloadSource.Official, CurseForgeApiKey = "fixture-unavailable", IncludeOptionalMods = false };
        var snapshot = DownloadAvailability.Snapshot(settings); settings.JavaPath = "changed";
        check(snapshot.CurseForgeApiKey == "fixture-unavailable" && snapshot.JavaPath == "fixture-java" && snapshot.ParallelDownloads == 16 && !snapshot.IncludeOptionalMods, "Integrated download settings freeze runtime options and retain the configured credential");
        check(new LauncherState().DownloadSettings().CurseForgeApiKey == "", "Fresh launcher download settings start with an empty CurseForge credential");
        var payload = Encoding.UTF8.GetBytes("ISLAND download fixture");
        JsonObject Artifact(string url, byte[] bytes) => new() { ["url"] = url, ["sha1"] = Convert.ToHexString(SHA1.HashData(bytes)), ["size"] = bytes.Length };
        var index = Encoding.UTF8.GetBytes("{\"objects\":{}}");
        var version = new JsonObject { ["id"] = "1.20.1", ["mainClass"] = "Fixture", ["downloads"] = new JsonObject { ["client"] = Artifact("https://fixture.invalid/client", payload) }, ["libraries"] = new JsonArray(), ["assetIndex"] = Artifact("https://fixture.invalid/index", index) };
        version["assetIndex"]!["id"] = "fixture";
        var versionBytes = Encoding.UTF8.GetBytes(version.ToJsonString());
        var manifest = new JsonObject { ["versions"] = new JsonArray(new JsonObject { ["id"] = "1.20.1", ["type"] = "release", ["url"] = "https://piston-meta.mojang.com/v1/packages/fixture/1.20.1.json", ["sha1"] = Convert.ToHexString(SHA1.HashData(versionBytes)), ["releaseTime"] = "2023-06-12T00:00:00Z" }) };
        byte[] PackBytes(bool curseForge)
        {
            using var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                using (var writer = new StreamWriter(archive.CreateEntry(curseForge ? "manifest.json" : "modrinth.index.json").Open()))
                    writer.Write(curseForge ? """{"manifestType":"minecraftModpack","manifestVersion":1,"name":"CF fixture","minecraft":{"version":"1.20.1","modLoaders":[{"id":"forge-47.2.0","primary":true}]},"files":[],"overrides":"overrides"}""" : """{"formatVersion":1,"game":"minecraft","name":"Integrated Pack","versionId":"1.0","dependencies":{"minecraft":"1.20.1"},"files":[]}""");
                using var config = new StreamWriter(archive.CreateEntry("overrides/config/island.txt").Open()); config.Write("owned-fixture");
            }
            return memory.ToArray();
        }
        var packBytes = PackBytes(false);
        var release = new JsonObject
        {
            ["id"] = "release1", ["name"] = "Fixture release", ["game_versions"] = new JsonArray("1.20.1"), ["loaders"] = new JsonArray("fabric"), ["version_type"] = "release", ["date_published"] = "2026-01-01T00:00:00Z",
            ["files"] = new JsonArray(new JsonObject { ["filename"] = "fixture.mrpack", ["primary"] = true, ["url"] = "https://cdn.modrinth.com/fixture.mrpack", ["size"] = packBytes.Length, ["hashes"] = new JsonObject { ["sha1"] = Convert.ToHexString(SHA1.HashData(packBytes)), ["sha512"] = Convert.ToHexString(SHA512.HashData(packBytes)) } })
        };
        var content = new Dictionary<string, byte[]>
        {
            [MinecraftInstaller.ManifestUrl] = Encoding.UTF8.GetBytes(manifest.ToJsonString()), ["https://piston-meta.mojang.com/v1/packages/fixture/1.20.1.json"] = versionBytes,
            ["https://fixture.invalid/client"] = payload, ["https://fixture.invalid/index"] = index, ["https://cdn.modrinth.com/fixture.mrpack"] = packBytes
        };
        var requests = new ConcurrentBag<string>(); var keySent = false;
        var cacheDirectory = Path.Combine(directory, "launcher-data");
        var cachePath = Path.Combine(cacheDirectory, "minecraft-versions.json");
        LauncherDownloads NewService() => new(options => new DownloadClient(options, new Handler(request =>
        {
            var url = request.RequestUri!; requests.Add(url.AbsoluteUri); keySent |= request.Headers.Contains("x-api-key");
            if (url.Host == "api.modrinth.com" && url.AbsolutePath == "/v2/search") return new(HttpStatusCode.OK) { Content = new StringContent("""{"total_hits":41,"hits":[{"project_id":"fixture","slug":"fixture","title":"Fixture Pack","description":"A fixture","downloads":123,"versions":["1.20.1"],"categories":["fabric"]}]}""") };
            if (url.AbsolutePath == "/v2/project/fixture/version") return new(HttpStatusCode.OK) { Content = new StringContent(new JsonArray(release.DeepClone()).ToJsonString()) };
            if (content.TryGetValue(url.AbsoluteUri, out var bytes)) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            throw new Exception("Unexpected download destination: " + url);
        })), cacheDirectory);
        var service = NewService();
        var games = await service.GetGamesAsync(snapshot, default);
        check(games.Single().Id == "1.20.1", "Integrated downloader reads the official game manifest through the existing installer");
        service = NewService();
        check((await service.GetGamesAsync(snapshot, default)).SequenceEqual(games) && File.Exists(cachePath)
            && requests.Count(url => url == MinecraftInstaller.ManifestUrl) == 1,
            "Restarted download services reuse the saved vanilla version list without another manifest request");
        var filter = new PackSearchFilter("fixture", "1.20.1", PackPlatform.Modrinth, LoaderKind.Fabric, PackSort.Downloads);
        var page = await service.SearchAsync(snapshot, filter, 2, default);
        check(page.Projects.Single().Platform == PackPlatform.Modrinth && page.Page == 2 && !page.HasNext && page.Total == 41
            && requests.Any(url => Uri.UnescapeDataString(url).Contains("categories:fabric") && url.Contains("offset=40") && url.Contains("index=downloads")), "Modrinth pack search preserves version, loader, sort and page filters");
        var project = page.Projects.Single(); var releases = await service.GetReleasesAsync(snapshot, project, filter, default);
        check(releases.Versions.Single().Id == "release1", "Integrated downloader reads and filters real-format pack releases");
        var pack = await service.PrepareAsync(snapshot, project, releases.Versions.Single(), default);
        check(pack.Format == PackFormat.Modrinth && pack.MinecraftVersion == "1.20.1" && File.ReadAllBytes(pack.ArchivePath).SequenceEqual(packBytes), "Pack preparation downloads, verifies SHA-1/SHA-512 and inspects the mrpack before confirmation");
        var target = await service.InstallAsync(snapshot, new("1.20.1", "Integrated game"), null, new Progress<InstallProgress>(), default);
        check(File.ReadAllBytes(Path.Combine(target, "Integrated game.jar")).SequenceEqual(payload) && File.Exists(Path.Combine(target, "Integrated game.json")), "Integrated worker completes an actual miniature game installation and atomic instance commit");
        var packed = await service.InstallAsync(snapshot, new("1.20.1", "Integrated pack"), pack, new Progress<InstallProgress>(), default);
        check(File.ReadAllText(Path.Combine(packed, "config", "island.txt")) == "owned-fixture", "Integrated worker applies local pack overrides into the installed instance");
        check(requests.Count(url => url == MinecraftInstaller.ManifestUrl) == 1,
            "Installing a known version reuses the cached list while still downloading version files");

        content[MinecraftInstaller.ManifestUrl] = "{\"versions\":[]}"u8.ToArray();
        var emptyRejected = false;
        try { await service.RefreshGamesAsync(snapshot, default); }
        catch (InvalidDataException) { emptyRejected = true; }
        check(emptyRejected && (await NewService().GetGamesAsync(snapshot, default)).SequenceEqual(games),
            "An invalid manual refresh leaves the last successful on-disk version list intact");
        manifest["versions"]!.AsArray().Insert(0, new JsonObject
        {
            ["id"] = "1.21", ["type"] = "release", ["url"] = "https://piston-meta.mojang.com/v1/packages/fixture/1.21.json"
        });
        content[MinecraftInstaller.ManifestUrl] = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        var refreshed = await service.RefreshGamesAsync(snapshot, default);
        check(refreshed[0].Id == "1.21" && (await NewService().GetGamesAsync(snapshot, default)).SequenceEqual(refreshed),
            "Manual refresh atomically replaces the list and the next launcher reads the updated version");
        var cfProject = project with { Platform = PackPlatform.CurseForge }; var before = requests.Count;
        snapshot.CurseForgeApiKey = "";
        var mixed = await service.SearchAsync(snapshot, filter with { Platform = PackPlatform.All }, 0, default);
        check(mixed.Projects.Count == 1 && mixed.Notices.Any(notice => notice.Contains("CurseForge")), "All-platform search keeps Modrinth results when the CurseForge credential is missing");
        before = requests.Count;
        await Reject(() => service.GetReleasesAsync(snapshot, cfProject, filter, default), check, "Missing-credential CurseForge release browsing is blocked before networking");
        await Reject(() => service.PrepareAsync(snapshot, cfProject, releases.Versions.Single(), default), check, "Missing-credential CurseForge archive downloads are blocked before networking");
        var cfFile = Path.Combine(directory, "curseforge.zip"); File.WriteAllBytes(cfFile, PackBytes(true));
        check((await service.InspectAsync(cfFile, default)).Format == PackFormat.CurseForge, "Local CurseForge manifests can be inspected before requesting a credential");
        var cfPack = new PackInfo { ArchivePath = cfFile, Name = "CF", MinecraftVersion = "1.20.1", Format = PackFormat.CurseForge, Manifest = new() };
        await Reject(() => service.InstallAsync(snapshot, new("1.20.1", "CF blocked"), cfPack, new Progress<InstallProgress>(), default), check, "The installation worker requires a key for CurseForge packs");
        check(requests.Count == before && !keySent && requests.All(url => !new Uri(url).Host.Contains("curseforge")), "Missing-credential actions send no CurseForge requests, and Modrinth receives no key");
    }
}
