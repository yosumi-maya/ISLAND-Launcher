using BlockDock.Core;
using Island.Core;

internal static class JavaRuntimeCacheTests
{
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "java-cache"); Directory.CreateDirectory(folder);
        JavaInstallation Runtime(string name, int major)
        {
            var directory = Path.Combine(folder, name, "bin"); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "java.exe"); File.WriteAllText(path, name);
            return new(path, major + ".0.1", major, "x64", "Fixture");
        }
        var j17 = Runtime("jdk17", 17); var j21 = Runtime("jdk21", 21); var j25 = Runtime("jdk25", 25);
        IReadOnlyList<JavaInstallation> installed = [j17, j21]; var scans = 0; var probes = 0;
        async Task<IReadOnlyList<JavaInstallation>> Discover(string _, string __, CancellationToken token) { scans++; await Task.Delay(15, token); return installed; }
        Task<JavaInstallation?> Probe(string path, CancellationToken token) { token.ThrowIfCancellationRequested(); probes++; return Task.FromResult(installed.FirstOrDefault(java => java.Path == path && File.Exists(path))); }
        var cacheFolder = Path.Combine(folder, "saved");
        var first = new JavaRuntimeCache(cacheFolder, Discover, Probe);
        await Task.WhenAll(first.GetAvailableAsync(root, "", default), first.GetAvailableAsync(root, "", default));
        check(scans == 1 && probes == 0 && File.Exists(Path.Combine(cacheFolder, "java-runtimes.json")), "Concurrent first-time Java list requests share one scan and persist the results without probing again");
        var restarted = new JavaRuntimeCache(cacheFolder, Discover, Probe);
        check((await restarted.GetAvailableAsync(root, "", default)).Count == 2 && scans == 1 && probes == 0, "A new Java catalog restores the saved list without scanning or executing Java");
        var automatic = await restarted.ResolveAsync(new() { GameDirectory = root }, new(21), new(), default);
        check(automatic.Path == j21.Path && scans == 1 && probes == 1, "Cached automatic selection validates only the chosen compatible JVM");
        try { await restarted.ResolveAsync(new() { GameDirectory = root, JavaPath = j17.Path }, new(21), new(), default); throw new Exception("Incompatible manual Java accepted"); }
        catch (InvalidOperationException) { check(scans == 1, "A valid but incompatible manual selection stays explicit instead of being silently replaced"); }
        check((await restarted.ResolveAsync(new() { GameDirectory = root, JavaPath = j17.Path }, new(21), new() { SkipCompatibilityCheck = true }, default)).Path == j17.Path,
            "The explicit compatibility-check bypass remains available for an existing manual Java");
        File.Delete(j21.Path); installed = [j17, j25]; var missing = new List<string>();
        var replacement = await restarted.ResolveAsync(new() { GameDirectory = root }, new(21), new(), default, j21.Path, missing.Add);
        check(replacement.Path == j25.Path && scans == 2 && missing.SequenceEqual([j21.Path]), "Deleting the remembered Java reports its exact path, rescans once and selects a compatible replacement");
        missing.Clear(); replacement = await restarted.ResolveAsync(new() { GameDirectory = root, JavaPath = j21.Path }, new(21), new(), default, unavailable: missing.Add);
        check(replacement.Path == j25.Path && scans == 3 && missing.SequenceEqual([j21.Path]), "A deleted manual Java also recovers through one automatic rescan");
        var afterRecovery = new JavaRuntimeCache(cacheFolder, Discover, Probe);
        check(afterRecovery.Snapshot().Items.All(java => java.Path != j21.Path) && !afterRecovery.Snapshot().NeedsRefresh,
            "A recovered catalog persists the replacement list and removes deleted paths across restarts");
        File.AppendAllText(j25.Path, "updated"); installed = [j17, j25 with { Version = "26.0.1", Major = 26 }];
        check(afterRecovery.Snapshot().NeedsRefresh && !afterRecovery.Snapshot().Items.Any(java => java.Path == j25.Path), "An updated Java executable invalidates its saved metadata before it is displayed or selected");
        await afterRecovery.GetAvailableAsync(root, "", default);
        check(scans == 4 && new JavaRuntimeCache(cacheFolder, Discover, Probe).Snapshot().Items.Any(java => java.Major == 26), "Refreshing changed Java metadata persists its new version");
        using (var locked = new FileStream(Path.Combine(cacheFolder, "java-runtimes.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            check((await afterRecovery.GetAvailableAsync(root, "", default, true)).Count == 2 && afterRecovery.SaveWarning is not null,
                "A locked cache file reports a save warning but keeps usable detection results available");
        }
        File.WriteAllText(Path.Combine(cacheFolder, "java-runtimes.json"), "{corrupt");
        var damaged = new JavaRuntimeCache(cacheFolder, Discover, Probe);
        check(damaged.Snapshot().NeedsRefresh && (await damaged.GetAvailableAsync(root, "", default)).Count == 2,
            "A damaged technical cache is safely rebuilt without touching launcher settings");
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); var before = scans; var beforeProbes = probes;
        try { await damaged.ResolveAsync(new() { GameDirectory = root, JavaPath = j17.Path }, new(8), new(), canceled.Token); throw new Exception("Canceled Java resolution accepted"); }
        catch (OperationCanceledException) { check(scans == before && probes == beforeProbes, "Canceled Java resolution neither scans nor probes the saved path"); }
        try { await damaged.GetAvailableAsync(root, "", canceled.Token); throw new Exception("Canceled Java list accepted"); }
        catch (OperationCanceledException) { check(scans == before, "Cancellation is honored even for a valid saved Java list"); }
        installed = [];
        try { await damaged.ResolveAsync(new() { GameDirectory = root, JavaPath = j21.Path }, new(21), new(), default); throw new Exception("No Java accepted"); }
        catch (InvalidOperationException ex) { check(scans == before + 1 && ex.Message.Contains("未找到可用的 64 位 Java 21"), "Recovery with no compatible Java stops with an actionable error after one scan"); }

        var upgradeFolder = Path.Combine(folder, "upgrade"); var upgradeScans = 0;
        IReadOnlyList<JavaInstallation> upgraded = [j17];
        Task<IReadOnlyList<JavaInstallation>> DiscoverUpgrade(string _, string __, CancellationToken token)
        { token.ThrowIfCancellationRequested(); upgradeScans++; return Task.FromResult(upgraded); }
        await new JavaRuntimeCache(upgradeFolder, DiscoverUpgrade, Probe).GetAvailableAsync(root, "", default);
        var savedPath = Path.Combine(upgradeFolder, "java-runtimes.json");
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(savedPath))!.AsObject();
        legacy.Remove("DiscoveryRevision"); File.WriteAllText(savedPath, legacy.ToJsonString());
        var zulu = Runtime("zulu25-win_x64", 25) with { Vendor = "Azul Systems, Inc." };
        upgraded = [j17, zulu];
        var upgrade = new JavaRuntimeCache(upgradeFolder, DiscoverUpgrade, Probe);
        check(upgrade.Snapshot() is { NeedsRefresh: true, Items.Count: 1 }, "An old Java discovery cache retains its valid entries while requesting one upgraded scan");
        check((await upgrade.GetAvailableAsync(root, "", default)).Any(java => java.Path == zulu.Path) && upgradeScans == 2,
            "Upgrading discovery adds previously missed Zulu runtimes without deleting launcher settings");
        var reopen = new JavaRuntimeCache(upgradeFolder, DiscoverUpgrade, Probe);
        check(!reopen.Snapshot().NeedsRefresh && (await reopen.GetAvailableAsync(root, "", default)).Count == 2 && upgradeScans == 2,
            "The upgraded discovery cache is saved and reused on the next launch without rescanning");
    }
}
