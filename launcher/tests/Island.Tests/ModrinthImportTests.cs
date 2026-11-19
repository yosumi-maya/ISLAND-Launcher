using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BlockDock.Core;

internal static class ModrinthImportTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(send(request));
    }
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        var directory = Path.Combine(root, "mixed-cdn-pack"); Directory.CreateDirectory(directory);
        var archive = Path.Combine(directory, "pack.mrpack");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("overrides/config/fixture.txt").Open())) writer.Write("config");
        var payload = Encoding.UTF8.GetBytes("verified mod payload");
        JsonObject FileEntry(string path, params string[] urls) => new()
        {
            ["path"] = path, ["fileSize"] = (long)payload.Length,
            ["downloads"] = new JsonArray(urls.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray()),
            ["hashes"] = new JsonObject { ["sha1"] = Convert.ToHexString(SHA1.HashData(payload)), ["sha512"] = Convert.ToHexString(SHA512.HashData(payload)) }
        };
        var manifest = new JsonObject { ["files"] = new JsonArray(
            FileEntry("mods/mixed.jar", "https://mediafilez.forgecdn.net/files/1/mod.jar", "https://edge.forgecdn.net/files/1/mod.jar", "https://cdn.modrinth.com/data/test/mod.jar"),
            FileEntry("mods/edge.jar", "https://edge.forgecdn.net/files/2/mod.jar")) };
        var pack = new PackInfo { Name = "Mixed CDN", Format = PackFormat.Modrinth, ArchivePath = archive, MinecraftVersion = "1.20.1", Manifest = manifest };
        var seen = new List<string>();
        using var network = new DownloadClient(new() { ParallelDownloads = 1, Source = DownloadSource.Official }, new Handler(request =>
        {
            var host = request.RequestUri!.Host; seen.Add(host);
            return host == "mediafilez.forgecdn.net" ? new(HttpStatusCode.NotFound) : new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        }));
        var installer = new ModpackInstaller(new(), network);
        var target = Path.Combine(directory, "instance");
        await installer.ApplyAsync(pack, target, null, default);
        check(seen.Contains("mediafilez.forgecdn.net") && seen.Contains("edge.forgecdn.net") && File.ReadAllBytes(Path.Combine(target, "mods/mixed.jar")).SequenceEqual(payload), "Mixed CurseForge and Modrinth mrpack URLs are accepted and fall back after a failed mirror");
        check(File.Exists(Path.Combine(target, "mods/edge.jar")) && File.ReadAllText(Path.Combine(target, "config/fixture.txt")) == "config", "A CurseForge-CDN-only mrpack entry installs alongside overrides");
        foreach (var url in new[] { "http://edge.forgecdn.net/file.jar", "https://edge.forgecdn.net.evil.invalid/file.jar" })
        {
            manifest["files"] = new JsonArray(FileEntry("mods/rejected.jar", url));
            var before = seen.Count; var rejected = false;
            try { await installer.ApplyAsync(pack, Path.Combine(directory, "rejected"), null, default); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected && seen.Count == before, "Unsafe scheme or lookalike CDN host is rejected before downloading: " + url);
        }
        manifest["files"] = new JsonArray(FileEntry("../outside.jar", "https://edge.forgecdn.net/files/1/mod.jar"));
        var escaped = false;
        try { await installer.ApplyAsync(pack, target, null, default); } catch (InvalidDataException) { escaped = true; }
        check(escaped, "Mixed-CDN imports still reject paths outside the instance");
        manifest["files"] = new JsonArray(FileEntry("mods/corrupt.jar", "https://edge.forgecdn.net/files/1/mod.jar"));
        using var corruptNetwork = new DownloadClient(new(), new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("corrupt mod payload!")) }));
        var corrupt = false;
        try { await new ModpackInstaller(new(), corruptNetwork).ApplyAsync(pack, target, null, default); } catch (Exception ex) when (ex is IOException or OperationCanceledException) { corrupt = true; }
        check(corrupt && !File.Exists(Path.Combine(target, "mods/corrupt.jar")), "Corrupt CDN payloads fail verification without installing a jar");
    }
}
