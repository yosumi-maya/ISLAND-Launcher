using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace BlockDock.Core;

public static class JavaDiscovery
{
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    { "mods", "saves", "assets", "libraries", "logs", "cache", "screenshots", "node_modules", ".git", ".blockdock", "resourcepacks", "shaderpacks", "versions" };

    public static IReadOnlyList<string> CollectCandidates(JavaSearchPaths paths, CancellationToken token)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.DirectCandidates) { token.ThrowIfCancellationRequested(); Add(path); }
        var queue = new Queue<(string Path, int Depth)>();
        var enqueued = 0;
        foreach (var root in paths.Roots.Take(2000)) if (!string.IsNullOrWhiteSpace(root)) { queue.Enqueue((root, 0)); enqueued++; }
        while (queue.Count > 0 && visited.Count < 2000 && found.Count < 128)
        {
            token.ThrowIfCancellationRequested();
            var (folder, depth) = queue.Dequeue();
            try
            {
                folder = Path.GetFullPath(folder);
                if (!visited.Add(folder) || !Directory.Exists(folder) || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                Add(folder);
                if (depth >= 5) continue;
                foreach (var child in Directory.EnumerateDirectories(folder).Take(2000 - enqueued))
                    if (!SkippedFolders.Contains(Path.GetFileName(child))) { queue.Enqueue((child, depth + 1)); enqueued++; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
        return found.ToArray();

        void Add(string raw)
        {
            if (found.Count >= 128 || string.IsNullOrWhiteSpace(raw)) return;
            try
            {
                var path = Normalize(raw);
                if (File.Exists(path)) found.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
    }

    private static string Normalize(string raw)
    {
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"')));
        if (Directory.Exists(path)) path = Path.Combine(path, File.Exists(Path.Combine(path, "java.exe")) ? "java.exe" : "bin/java.exe");
        if (Path.GetFileName(path).Equals("javaw.exe", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(Path.GetDirectoryName(path)!, "java.exe");
        return Path.GetFullPath(path);
    }

    public static async Task<JavaInstallation?> ProbeAsync(string path, CancellationToken token, TimeSpan? timeout = null)
    {
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(4));
        using var process = new Process();
        Task<string>? stdout = null, stderr = null;
        var started = false;
        try
        {
            path = Normalize(path);
            if (!File.Exists(path)) return null;
            var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-XshowSettings:properties"); start.ArgumentList.Add("-version");
            foreach (var variable in new[] { "JAVA_ARGS", "CLASSPATH", "CONFIGPATH", "JAVA_HOME", "JRE_HOME", "_JAVA_OPTIONS", "JAVA_OPTIONS", "JAVA_TOOL_OPTIONS", "JDK_JAVA_OPTIONS" }) start.Environment.Remove(variable);
            process.StartInfo = start;
            started = process.Start();
            if (!started) return null;
            stdout = ReadBoundedAsync(process.StandardOutput, deadline.Token);
            stderr = ReadBoundedAsync(process.StandardError, deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            var output = await stdout + "\n" + await stderr;
            if (process.ExitCode != 0) return null;
            string Property(string name) => Regex.Match(output, @"(?m)^\s*" + Regex.Escape(name) + @"\s*=\s*([^\r\n]+)").Groups[1].Value.Trim();
            var version = Property("java.version");
            var match = Regex.Match(version, @"^(?:1\.)?(\d+)");
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var major) || major < 2) return null;
            var architecture = Property("os.arch").ToLowerInvariant() switch
            {
                "amd64" or "x86_64" or "x64" => "x64", "aarch64" or "arm64" => "arm64",
                "x86" or "i386" or "i486" or "i586" or "i686" => "x86", _ => "unknown"
            };
            var home = Property("java.home");
            // Oracle PATH wrappers can point to an existing JRE. Show the actual runtime once.
            if (home.Length > 0 && Directory.Exists(home) && File.Exists(Path.Combine(home, "bin", "java.exe"))) path = Normalize(home);
            return new(path, version, major, architecture, Property("java.vendor"));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException) { return null; }
        finally
        {
            if (started && !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)); }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or TimeoutException) { }
            }
            foreach (var read in new[] { stdout, stderr }) if (read is not null)
                try { await read; } catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var result = new StringBuilder();
        var buffer = new char[2048];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
            if (result.Length < 65536) result.Append(buffer, 0, Math.Min(read, 65536 - result.Length));
        return result.ToString();
    }

    public static async Task<IReadOnlyList<JavaInstallation>> DiscoverAsync(JavaSearchPaths paths, CancellationToken token)
    {
        var candidates = await Task.Run(() => CollectCandidates(paths, token), token);
        var results = new System.Collections.Concurrent.ConcurrentBag<JavaInstallation>();
        await Parallel.ForEachAsync(candidates, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (path, ct) =>
        {
            var java = await ProbeAsync(path, ct);
            if (java is not null) results.Add(java);
        });
        return results.DistinctBy(java => java.Path, StringComparer.OrdinalIgnoreCase).OrderByDescending(java => java.Major).ThenBy(java => java.IsEarlyAccess).ThenByDescending(java => VersionKey(java.Version), StringComparer.Ordinal).ThenBy(java => java.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string VersionKey(string version) => string.Join('.', Regex.Matches(version, @"\d+").Select(m => m.Value.PadLeft(10, '0')));

    public static JavaInstallation Select(IEnumerable<JavaInstallation> candidates, JavaRequirement requirement) => candidates
        .Where(java => java.Is64Bit && java.Major >= requirement.MinimumMajor)
        .OrderBy(java => java.Major).ThenBy(java => java.IsEarlyAccess).ThenByDescending(java => VersionKey(java.Version), StringComparer.Ordinal)
        .ThenBy(java => java.Path, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
        ?? throw new InvalidOperationException($"未找到可用的 64 位 Java {requirement.MinimumMajor} 或更高版本。请安装 Java，随后在设置中重新检测或手动选择 java.exe。");

    public static async Task<JavaInstallation> ResolveAsync(AppSettings settings, JavaRequirement requirement, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(settings.JavaPath))
            return Select(await DiscoverAsync(JavaSearchPaths.ForGameDirectory(settings.GameDirectory, ""), token), requirement);
        var java = await ProbeAsync(settings.JavaPath, token)
            ?? throw new InvalidOperationException("设置中指定的 Java 无法运行或探测超时，请重新选择 java.exe，或改用自动选择。");
        if (!java.Is64Bit || java.Major < requirement.MinimumMajor)
            throw new InvalidOperationException($"所选 Java {java.Version}（{java.Architecture}）不符合安装要求，需要 64 位 Java {requirement.MinimumMajor} 或更高版本。请在设置中更换 Java 或改用自动选择。");
        return java;
    }
}
