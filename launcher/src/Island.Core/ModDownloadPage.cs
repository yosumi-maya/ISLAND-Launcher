using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BlockDock.Core;

namespace Island.Core;

public static class ModDownloadPage
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(12),
        DefaultRequestHeaders = { { "User-Agent", "yosumi-maya/ISLAND-Launcher/1.1.0" } }
    };

    public static Task<string?> FindAsync(string instance, ModFile mod, CancellationToken token)
        => FindAsync(instance, mod, Client, token);

    internal static async Task<string?> FindAsync(string instance, ModFile mod, HttpClient client, CancellationToken token)
    {
        var path = ModFiles.ExistingPath(instance, mod);
        var records = ReadRecords(instance);
        var size = new FileInfo(path).Length;
        var possible = records.Where(file => file.Kind == ContentKind.Mod && file.Size == size)
            .OrderByDescending(file => string.Equals(file.FileName, mod.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        string sha1;
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            sha1 = Convert.ToHexString(await SHA1.HashDataAsync(stream, token).ConfigureAwait(false));
            string? sha512 = null;
            foreach (var file in possible)
            {
                if (!string.IsNullOrEmpty(file.Sha512))
                {
                    if (sha512 is null)
                    {
                        stream.Position = 0;
                        sha512 = Convert.ToHexString(await SHA512.HashDataAsync(stream, token).ConfigureAwait(false));
                    }
                    if (!sha512.Equals(file.Sha512, StringComparison.OrdinalIgnoreCase)) continue;
                }
                else if (!sha1.Equals(file.Sha1, StringComparison.OrdinalIgnoreCase)) continue;

                var recorded = file.Platform switch
                {
                    PackPlatform.Modrinth => ModrinthPage(file.ProjectSlug) ?? ModrinthPage(file.ProjectId),
                    PackPlatform.CurseForge => CurseForgePage(file.ProjectSlug),
                    _ => null
                };
                if (recorded is not null) return recorded;
            }
        }

        foreach (var metadata in ModMetadataReader.Read(path))
            if (MetadataPage(metadata.Homepage) is { } page) return page;

        using var lookup = await client.GetAsync("https://api.modrinth.com/v2/version_file/" + sha1.ToLowerInvariant() + "?algorithm=sha1", token).ConfigureAwait(false);
        if (lookup.StatusCode == HttpStatusCode.NotFound) return null;
        lookup.EnsureSuccessStatusCode();
        var version = await lookup.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: token).ConfigureAwait(false);
        return version?["project_id"] is JsonValue id && id.TryGetValue<string>(out var project) ? ModrinthPage(project) : null;
    }

    private static IReadOnlyList<ContentFile> ReadRecords(string instance)
    {
        try { return ContentDownloads.ReadInstalled(instance); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return []; }
    }

    private static string? MetadataPage(string homepage)
    {
        if (!Uri.TryCreate(homepage, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0) return null;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (uri.Host is "modrinth.com" or "www.modrinth.com" && parts is ["mod", var mod, ..])
            return ModrinthPage(mod);
        if (uri.Host is "curseforge.com" or "www.curseforge.com" && parts is ["minecraft", "mc-mods", var slug, ..])
            return CurseForgePage(slug);
        return null;
    }

    private static bool IsProjectKey(string? value) => value is { Length: > 0 and <= 128 } && Regex.IsMatch(value, @"\A[A-Za-z0-9_-]+\z");
    private static string? ModrinthPage(string? project) => IsProjectKey(project) ? $"https://modrinth.com/mod/{project}/versions" : null;
    private static string? CurseForgePage(string? slug) => IsProjectKey(slug) ? $"https://www.curseforge.com/minecraft/mc-mods/{slug}/files" : null;
}
