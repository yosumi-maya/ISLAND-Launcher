using System.Text.Json;
using BlockDock.Core;

namespace Island.Core;

internal sealed class GameVersionCache(string? directory)
{
    private const int MaximumEntries = 4096;
    private const long MaximumBytes = 4 * 1024 * 1024;
    private readonly string? path = directory is null ? null : Path.Combine(directory, "minecraft-versions.json");
    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyList<GameVersion>? current;

    internal async Task<IReadOnlyList<GameVersion>> GetAsync(Func<CancellationToken, Task<IReadOnlyList<GameVersion>>> fetch,
        CancellationToken token, bool refresh = false, string? requiredVersion = null)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!refresh)
            {
                current ??= await ReadAsync(token).ConfigureAwait(false);
                if (current is not null && (requiredVersion is null || current.Any(entry => entry.Id == requiredVersion)))
                    return current;
            }

            var versions = await fetch(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!IsUsable(versions)) throw new InvalidDataException("服务器返回的 Minecraft 版本列表为空或格式不正确。");
            var snapshot = Array.AsReadOnly(versions.ToArray());
            if (path is not null) await SaveAsync(path, snapshot, token).ConfigureAwait(false);
            current = snapshot;
            return snapshot;
        }
        finally { gate.Release(); }
    }

    private async Task<IReadOnlyList<GameVersion>?> ReadAsync(CancellationToken token)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (file.Length > MaximumBytes) return null;
            var entries = await JsonSerializer.DeserializeAsync<GameVersion[]>(file, cancellationToken: token).ConfigureAwait(false);
            return entries is not null && IsUsable(entries) && entries.All(IsTrustedMetadata) ? Array.AsReadOnly(entries) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static bool IsUsable(IReadOnlyList<GameVersion> entries) => entries.Count is > 0 and <= MaximumEntries
        && entries.All(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Id) && !string.IsNullOrWhiteSpace(entry.Type)
            && Uri.TryCreate(entry.Url, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps
            && (entry.Sha1 is null || entry.Sha1.Length == 40 && entry.Sha1.All(Uri.IsHexDigit)));

    private static bool IsTrustedMetadata(GameVersion entry) => Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && uri.UserInfo.Length == 0
        && uri.Host is "piston-meta.mojang.com" or "launchermeta.mojang.com" or "piston-data.mojang.com" or "launcher.mojang.com" or "bmclapi2.bangbang93.com";

    private static async Task SaveAsync(string path, IReadOnlyList<GameVersion> entries, CancellationToken token)
    {
        if (!entries.All(IsTrustedMetadata)) return;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                await JsonSerializer.SerializeAsync(file, entries, cancellationToken: token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A read-only profile can still reuse the freshly fetched list in this session.
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
