using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using BlockDock.Core;
using Island.Core;

internal static class ModDownloadPageTests
{
    internal static async Task RunAsync(string root, Action<bool, string> check)
    {
        var instance = Path.Combine(root, "mod-download-pages");
        var mods = Path.Combine(instance, "mods");
        Directory.CreateDirectory(mods);
        var renamed = Path.Combine(mods, "renamed.jar.disabled");
        var bytes = "known fixture"u8.ToArray();
        File.WriteAllBytes(renamed, bytes);
        var state = Path.Combine(instance, ".island");
        Directory.CreateDirectory(state);
        var sha1 = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
        var record = new ContentFile("123", "456", ContentKind.Mod, "Known Mod", "original.jar", "", bytes.Length, sha1, "")
        { Platform = PackPlatform.CurseForge, ProjectSlug = "known-mod" };
        File.WriteAllText(Path.Combine(state, "content.json"), JsonSerializer.Serialize(new[] { record }));

        var calls = 0;
        using var client = new HttpClient(new MockHandler(request =>
        {
            calls++;
            check(request.RequestUri!.Host == "api.modrinth.com" && request.RequestUri.AbsolutePath.EndsWith(sha1, StringComparison.Ordinal),
                "Manual mod lookup sends the exact file hash to the Modrinth API");
            return Task.FromResult("""{"project_id":"example-mod"}""");
        }));
        var mod = ModFiles.List(instance).Single();
        var page = await ModDownloadPage.FindAsync(instance, mod, client, CancellationToken.None);
        check(page == "https://www.curseforge.com/minecraft/mc-mods/known-mod/files" && calls == 0,
            "A renamed disabled mod retains its exact CurseForge download page through the stored hash");
        File.Delete(Path.Combine(state, "content.json"));
        page = await ModDownloadPage.FindAsync(instance, mod, client, CancellationToken.None);
        check(page == "https://modrinth.com/mod/example-mod/versions" && calls == 1,
            "An untracked mod opens the Modrinth project matched by its real hash");

        var local = Path.Combine(mods, "metadata.jar");
        using (var jar = ZipFile.Open(local, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(jar.CreateEntry("fabric.mod.json").Open()))
            writer.Write("""{"id":"local","contact":{"homepage":"https://modrinth.com/mod/local-name"}}""");
        page = await ModDownloadPage.FindAsync(instance, ModFiles.List(instance).Single(file => file.Path == local), client, CancellationToken.None);
        check(page == "https://modrinth.com/mod/local-name/versions" && calls == 1,
            "A declared Modrinth homepage resolves directly to the mod downloads");
        using var missing = new HttpClient(new MissingHandler());
        page = await ModDownloadPage.FindAsync(instance, mod, missing, CancellationToken.None);
        check(page is null, "An unknown file reports no download page");
    }

    private sealed class MissingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
