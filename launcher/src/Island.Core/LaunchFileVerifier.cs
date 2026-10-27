using System.Collections.Concurrent;
using System.Text.Json;
using BlockDock.Core;

namespace Island.Core;

// A successful hash verification can be reused while length, modification time
// and the expected metadata all remain unchanged. Modified files are rechecked.
public sealed class LaunchFileVerifier : IAsyncDisposable
{
    public sealed record Stamp(long Length, long Modified, string Expected);
    private readonly string root, cachePath;
    private readonly DownloadClient network;
    private readonly ConcurrentDictionary<string, Stamp> entries = new(StringComparer.OrdinalIgnoreCase);
    private int changed;
    public int CacheHits { get; private set; }
    private readonly object hitLock = new();

    public LaunchFileVerifier(string root, DownloadClient network)
    {
        this.root = Path.GetFullPath(root); this.network = network;
        cachePath = SafePaths.Under(root, ".island/verified-files.json");
        try
        {
            if (File.Exists(cachePath) && new FileInfo(cachePath).Length <= 32 * 1024 * 1024)
                foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, Stamp>>(File.ReadAllText(cachePath))?.Take(100000) ?? [])
                    if (pair.Value is not null) entries[pair.Key] = pair.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    public async Task EnsureAsync(TransferFile file, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var relative = Path.GetRelativePath(root, Path.GetFullPath(file.Path)).Replace('\\', '/');
        _ = SafePaths.Under(root, relative);
        var expected = file.Sha1 + ":" + file.Sha512 + ":" + file.Size;
        Stamp? ReadStamp()
        {
            var info = new FileInfo(file.Path);
            return info.Exists && info.Length > 0 ? new(info.Length, info.LastWriteTimeUtc.Ticks, expected) : null;
        }
        var hasHash = file.Sha1 is not null || file.Sha512 is not null;
        if (hasHash && entries.TryGetValue(relative, out var cached) && ReadStamp() == cached)
        {
            lock (hitLock) CacheHits++;
            return;
        }
        await network.DownloadAsync(file, token);
        if (hasHash && ReadStamp() is { } stamp && entries.Count < 100000) { entries[relative] = stamp; Interlocked.Exchange(ref changed, 1); }
    }

    public async Task EnsureManyAsync(IEnumerable<TransferFile> files, string stage, IProgress<InstallProgress>? progress, CancellationToken token)
    {
        var list = files.DistinctBy(file => Path.GetFullPath(file.Path), StringComparer.OrdinalIgnoreCase).ToArray();
        var done = 0;
        await Parallel.ForEachAsync(list, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = token }, async (file, ct) =>
        {
            await EnsureAsync(file, ct);
            progress?.Report(new(stage, Path.GetFileName(file.Path), Interlocked.Increment(ref done), list.Length));
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (changed == 0) return;
        var temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entries));
            File.Move(temporary, cachePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    }
}
