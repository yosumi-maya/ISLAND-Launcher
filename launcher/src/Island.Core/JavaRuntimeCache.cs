using System.Text.Json;
using BlockDock.Core;

namespace Island.Core;

public sealed record JavaCacheSnapshot(IReadOnlyList<JavaInstallation> Items, IReadOnlyList<string> MissingPaths, bool NeedsRefresh);

public sealed class JavaRuntimeCache
{
    private sealed record Entry(JavaInstallation Java, long Length, long Modified);
    private const int DiscoveryRevision = 2;
    private sealed record Saved(int Schema, Entry[] Entries, int DiscoveryRevision = 0);
    internal static JavaRuntimeCache Transient { get; } = new();
    private readonly string? path;
    private readonly Func<string, string, CancellationToken, Task<IReadOnlyList<JavaInstallation>>> discover;
    private readonly Func<string, CancellationToken, Task<JavaInstallation?>> probe;
    private readonly SemaphoreSlim gate = new(1, 1);
    private Entry[] entries = [];
    private bool known;
    public string? SaveWarning { get; private set; }

    public JavaRuntimeCache(string? directory = null,
        Func<string, string, CancellationToken, Task<IReadOnlyList<JavaInstallation>>>? discover = null,
        Func<string, CancellationToken, Task<JavaInstallation?>>? probe = null)
    {
        this.discover = discover ?? JavaRuntimeCatalog.DiscoverAsync;
        this.probe = probe ?? ((java, token) => JavaDiscovery.ProbeAsync(java, token));
        path = directory is null ? null : Path.Combine(directory, "java-runtimes.json");
        try
        {
            if (path is not null && File.Exists(path) && new FileInfo(path).Length <= 2 * 1024 * 1024
                && JsonSerializer.Deserialize<Saved>(File.ReadAllText(path)) is { Schema: 1, Entries: { Length: <= 256 } } saved)
            {
                entries = saved.Entries.Where(entry => entry?.Java is { } java && Path.IsPathFullyQualified(java.Path)
                    && java.Major is >= 1 and <= 100 && java.Version is not null && java.Architecture is not null && java.Vendor is not null)
                    .DistinctBy(entry => entry.Java.Path, StringComparer.OrdinalIgnoreCase).ToArray();
                known = saved.DiscoveryRevision == DiscoveryRevision;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
    }

    public JavaCacheSnapshot Snapshot()
    {
        var available = new List<JavaInstallation>(); var missing = new List<string>(); var changed = false;
        foreach (var entry in entries)
        {
            var stamp = Stamp(entry.Java);
            if (stamp is null) missing.Add(entry.Java.Path);
            else if (stamp.Length != entry.Length || stamp.Modified != entry.Modified) changed = true;
            else available.Add(entry.Java);
        }
        return new(available, missing, !known || entries.Length == 0 || changed || missing.Count > 0);
    }

    public async Task<IReadOnlyList<JavaInstallation>> GetAvailableAsync(string root, string configured, CancellationToken token, bool force = false)
    {
        await gate.WaitAsync(token);
        try
        {
            var cached = Snapshot();
            if (!force && !cached.NeedsRefresh) return cached.Items;
            var found = await discover(root, configured, token); token.ThrowIfCancellationRequested();
            entries = found.DistinctBy(java => java.Path, StringComparer.OrdinalIgnoreCase).Take(256)
                .Select(Stamp).OfType<Entry>().ToArray(); known = true; Save();
            return entries.Select(entry => entry.Java).ToArray();
        }
        finally { gate.Release(); }
    }

    public async Task<JavaInstallation> ResolveAsync(AppSettings settings, JavaRequirement requirement, JvmSettings options, CancellationToken token,
        string? previousAutomaticPath = null, Action<string>? unavailable = null)
    {
        token.ThrowIfCancellationRequested();
        var tested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Notify(string failed) { if (notified.Add(failed)) unavailable?.Invoke(failed); }
        var force = false;
        if (!string.IsNullOrWhiteSpace(settings.JavaPath))
        {
            tested.Add(settings.JavaPath);
            var selected = File.Exists(settings.JavaPath) ? await probe(settings.JavaPath, token) : null;
            if (selected is not null)
            {
                if (!options.SkipCompatibilityCheck && (!selected.Is64Bit || selected.Major < requirement.MinimumMajor))
                    throw new InvalidOperationException($"所选 Java {selected.Version}（{selected.Architecture}）不符合安装要求，需要 64 位 Java {requirement.MinimumMajor} 或更高版本。请在设置中更换 Java 或改用自动选择。");
                return selected;
            }
            Notify(settings.JavaPath); force = true;
        }
        else if (!string.IsNullOrWhiteSpace(previousAutomaticPath))
        {
            tested.Add(previousAutomaticPath);
            var previous = File.Exists(previousAutomaticPath) ? await probe(previousAutomaticPath, token) : null;
            if (previous is { Is64Bit: true } && previous.Major >= requirement.MinimumMajor) return previous;
            if (previous is null) { Notify(previousAutomaticPath); force = true; }
        }
        foreach (var missing in Snapshot().MissingPaths.Take(1)) { Notify(missing); force = true; }
        for (var pass = 0; pass < 2; pass++)
        {
            var scanned = force || Snapshot().NeedsRefresh;
            var available = await GetAvailableAsync(settings.GameDirectory, "", token, force);
            var candidates = available.Where(java => !tested.Contains(java.Path) && java.Is64Bit && java.Major >= requirement.MinimumMajor).ToList();
            while (candidates.Count > 0)
            {
                var candidate = JavaDiscovery.Select(candidates, requirement); tested.Add(candidate.Path); candidates.Remove(candidate);
                var verified = File.Exists(candidate.Path) ? await probe(candidate.Path, token) : null;
                if (verified is { Is64Bit: true } && verified.Major >= requirement.MinimumMajor) return verified;
                if (verified is null) Notify(candidate.Path);
            }
            if (scanned) break;
            force = true;
        }
        return JavaDiscovery.Select([], requirement);
    }

    private static Entry? Stamp(JavaInstallation java)
    {
        try { var file = new FileInfo(java.Path); return file.Exists ? new(java, file.Length, file.LastWriteTimeUtc.Ticks) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }
    private void Save()
    {
        if (path is null) return;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Saved(1, entries, DiscoveryRevision))); File.Move(temporary, path, true); SaveWarning = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SaveWarning = "Java 检测列表未能保存：" + ex.Message; }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
