using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BlockDock.Core;

namespace Island.Core;

public sealed record LaunchIdentity(string Name, string Uuid, string AccessToken, bool Microsoft);
public sealed record LaunchPlan(ProcessStartInfo StartInfo, string NativesDirectory, IReadOnlyList<string> Arguments);

public static class GameLauncher
{
    public static async Task<bool> CleanupNativesAsync(string root, string directory)
    {
        var parent = SafePaths.Under(root, ".island/natives");
        if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _) || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(directory)), parent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("只能清理本启动器生成的临时原生库目录。");
        SafePaths.Under(parent, Path.GetFileName(directory));
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); return true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { if (attempt < 3) await Task.Delay(250); }
        }
        return false;
    }

    public static async Task RepairAsync(string root, ResolvedVersion version, DownloadClient network, IProgress<InstallProgress>? progress, CancellationToken token, VersionIsolation isolation = VersionIsolation.Auto)
    {
        await using var verifier = new LaunchFileVerifier(root, network);
        await verifier.EnsureManyAsync(LaunchLibraries.Files(root, version.Profile), "检查游戏依赖", progress, token);
        var client = version.Chain.LastOrDefault(json => json["downloads"]?["client"] is JsonObject)?["downloads"]?["client"] as JsonObject;
        if (client is not null) await verifier.EnsureAsync(MinecraftInstaller.FromArtifact(version.ClientJar, client), token);
        if (!File.Exists(version.ClientJar)) throw new FileNotFoundException("游戏本体缺失，请重新安装对应版本。", version.ClientJar);
        if (version.Profile["logging"]?["client"]?["file"] is JsonObject logFile)
            await verifier.EnsureAsync(MinecraftInstaller.FromArtifact(SafePaths.Under(root, "assets/log_configs/" + logFile.Str("id")), logFile), token);
        if (version.Profile["assetIndex"] is not JsonObject assetInfo) return;
        var indexPath = SafePaths.Under(root, "assets/indexes/" + assetInfo.Str("id") + ".json");
        await verifier.EnsureAsync(MinecraftInstaller.FromArtifact(indexPath, assetInfo), token);
        var index = JsonNode.Parse(await File.ReadAllTextAsync(indexPath, token)) as JsonObject ?? throw new InvalidDataException("资源索引无效。");
        var objects = index.Object("objects");
        var files = objects.Select(pair =>
        {
            var hash = pair.Value.Str("hash");
            if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{40}$")) throw new InvalidDataException("资源索引包含无效哈希。");
            return new TransferFile(SafePaths.Under(root, $"assets/objects/{hash[..2]}/{hash}"), [$"https://resources.download.minecraft.net/{hash[..2]}/{hash}"], pair.Value.Long("size"), hash);
        });
        await verifier.EnsureManyAsync(files, "检查游戏资源", progress, token);
        var targets = new List<string>();
        if (index["virtual"]?.GetValue<bool>() == true) targets.Add(SafePaths.Under(root, "assets/virtual/" + assetInfo.Str("id")));
        if (index["map_to_resources"]?.GetValue<bool>() == true) targets.Add(SafePaths.Under(GameDirectories.Resolve(root, version.Id, isolation).Directory, "resources"));
        foreach (var targetRoot in targets)
        foreach (var (name, value) in objects)
        {
            token.ThrowIfCancellationRequested();
            var hash = value.Str("hash"); var target = SafePaths.Under(targetRoot, name);
            if (File.Exists(target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(SafePaths.Under(root, $"assets/objects/{hash[..2]}/{hash}"), target);
        }
    }

    public static LaunchPlan Prepare(string root, ResolvedVersion version, string java, int memoryMb, LaunchIdentity player, string? quickPlayWorld = null, VersionIsolation isolation = VersionIsolation.Auto, JvmSettings? options = null, int javaMajor = 8)
    {
        if (memoryMb is < 512 or > 65536) throw new ArgumentException("内存应在 512–65536 MB 之间。");
        if (!File.Exists(java)) throw new FileNotFoundException("Java 程序不存在。", java);
        options ??= new(); options.Validate();
        if (options.InitialMemoryMb > memoryMb) throw new ArgumentException("初始堆内存不能大于本次最大游戏内存。");
        var instance = GameDirectories.Resolve(root, version.Id, isolation).Directory;
        var natives = SafePaths.Under(root, ".island/natives/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(natives);
        try
        {
            var libraries = new List<string>();
            foreach (var library in version.Profile.Array("libraries").OfType<JsonObject>().Where(VersionProfile.Allowed))
            {
                var downloads = library["downloads"] as JsonObject;
                var artifact = downloads?["artifact"];
                if (LaunchLibraries.HasArtifact(root, library))
                {
                    var path = SafePaths.Under(root, "libraries/" + (artifact?.Str("path", SafePaths.Maven(library.Str("name"))) ?? SafePaths.Maven(library.Str("name"))));
                    if (!File.Exists(path)) throw new FileNotFoundException("缺少依赖文件，请联网启动以修复：" + Path.GetFileName(path), path);
                    libraries.Add(path);
                }
                var classifier = library["natives"].Str("windows").Replace("${arch}", "64");
                if (classifier.Length == 0 || downloads?["classifiers"]?[classifier] is not JsonObject native) continue;
                var archive = SafePaths.Under(root, "libraries/" + native.Str("path", SafePaths.Maven(library.Str("name") + ":" + classifier)));
                var exclude = library["extract"].Array("exclude").Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).Append("META-INF/").ToArray();
                ExtractNatives(archive, natives, exclude);
            }
            if (!File.Exists(version.ClientJar)) throw new FileNotFoundException("找不到 Minecraft 客户端。", version.ClientJar);
            libraries.Add(version.ClientJar);
            var assets = SafePaths.Under(root, "assets");
            var index = version.Profile["assetIndex"].Str("id", version.Profile.Str("assets", "legacy"));
            var legacyAssets = Directory.Exists(Path.Combine(assets, "virtual", index)) ? Path.Combine(assets, "virtual", index) : assets;
            var replacements = new Dictionary<string, string>
            {
                ["auth_player_name"] = player.Name, ["version_name"] = version.Id, ["game_directory"] = instance,
                ["assets_root"] = assets, ["assets_index_name"] = index, ["auth_uuid"] = player.Uuid,
                ["auth_access_token"] = player.AccessToken, ["auth_session"] = "token:" + player.AccessToken + ":" + player.Uuid,
                ["user_type"] = player.Microsoft ? "msa" : "legacy", ["version_type"] = version.Profile.Str("type", "release"),
                ["user_properties"] = "{}", ["natives_directory"] = natives, ["launcher_name"] = "IslandLauncher",
                ["launcher_version"] = "0.10.22", ["classpath"] = string.Join(Path.PathSeparator, libraries.Distinct(StringComparer.OrdinalIgnoreCase)),
                ["library_directory"] = SafePaths.Under(root, "libraries"), ["classpath_separator"] = Path.PathSeparator.ToString(),
                // HMCL adds this placeholder to Forge's ignoreList when normalizing imported profiles.
                ["primary_jar_name"] = Path.GetFileName(version.ClientJar),
                ["game_assets"] = legacyAssets, ["resolution_width"] = "1280", ["resolution_height"] = "720",
                ["auth_xuid"] = "", ["clientid"] = ""
            };
            var features = new Dictionary<string, bool>();
            var supportsQuickPlay = version.Profile["arguments"].Array("game").OfType<JsonObject>()
                .Any(argument => argument.Array("rules").Any(rule => rule?["features"]?["is_quick_play_singleplayer"]?.GetValue<bool>() == true));
            if (quickPlayWorld is not null && supportsQuickPlay && Directory.Exists(SafePaths.Under(instance, "saves/" + quickPlayWorld)))
            {
                var quickPlayLog = SafePaths.Under(instance, "quickPlay/island.json"); Directory.CreateDirectory(Path.GetDirectoryName(quickPlayLog)!);
                features["has_quick_plays_support"] = true; features["is_quick_play_singleplayer"] = true;
                replacements["quickPlaySingleplayer"] = quickPlayWorld; replacements["quickPlayPath"] = quickPlayLog;
            }
            string Expand(string text) => Regex.Replace(text, @"\$\{([^}]+)\}", match => replacements.TryGetValue(match.Groups[1].Value, out var value) ? value : throw new InvalidDataException("不支持的启动参数：" + match.Value));
            IEnumerable<string> Arguments(JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item is JsonValue value) yield return Expand(value.GetValue<string>());
                    else if (item is JsonObject condition && VersionProfile.Allowed(condition, features))
                    {
                        if (condition["value"] is JsonArray values) { foreach (var val in values) yield return Expand(val!.GetValue<string>()); }
                        else yield return Expand(condition.Str("value"));
                    }
                }
            }
            var custom = JvmArguments.Parse(options.ExtraArguments);
            var args = new List<string>();
            if (!options.OmitDefaultArguments) args.AddRange(["-Dfile.encoding=UTF-8", "-Dsun.stdout.encoding=UTF-8", "-Dsun.stderr.encoding=UTF-8"]);
            var jvm = Arguments(version.Profile["arguments"].Array("jvm")).ToList();
            static bool Collector(string argument) => Regex.IsMatch(argument, @"^-XX:[+-]Use\w*GC$");
            if (custom.Any(Collector) || (!options.OmitOptimizations && options.GarbageCollector != GarbageCollectorMode.Custom && javaMajor >= 8)) jvm.RemoveAll(Collector);
            if (!jvm.Any(argument => argument is "-cp" or "-classpath" or "--class-path" || argument.StartsWith("--class-path=", StringComparison.Ordinal))) jvm.AddRange(["-cp", replacements["classpath"]]);
            if (!jvm.Any(x => x.StartsWith("-Djava.library.path=", StringComparison.Ordinal))) jvm.Add("-Djava.library.path=" + natives);
            var configured = jvm.Concat(custom).ToArray();
            args.AddRange(GarbageCollectors.Arguments(options, javaMajor, configured));
            if (LaunchPatches.UseUnsafeAgent(options, javaMajor, version.Profile) && !configured.Any(argument => argument.StartsWith("-javaagent:", StringComparison.Ordinal) && argument.Contains("lwjgl-unsafe-agent", StringComparison.OrdinalIgnoreCase)))
                args.Add("-javaagent:" + LaunchPatches.Extract(root, false));
            // Metadata can contain inherited -cp switches; Java uses the final one, which is expanded from the merged libraries.
            args.AddRange(jvm);
            if (version.Profile["logging"]?["client"] is JsonObject logging && logging["file"] is JsonObject file)
                args.Add(logging.Str("argument").Replace("${path}", SafePaths.Under(root, "assets/log_configs/" + file.Str("id"))));
            args.AddRange([$"-Xms{options.InitialMemoryMb ?? 256}M", $"-Xmx{memoryMb}M"]);
            if (options.PermGenMb is { } perm && javaMajor < 8) args.Add($"-XX:MaxPermSize={perm}M");
            // Explicit user flags come last, so a chosen collector or heap size wins over generated defaults.
            args.AddRange(custom);
            if (LaunchPatches.UseWrapper(options, javaMajor)) args.AddRange(["-jar", LaunchPatches.Extract(root, true)]);
            args.Add(version.Profile.Str("mainClass"));
            var game = Arguments(version.Profile["arguments"].Array("game")).ToList();
            if (game.Count == 0) game.AddRange(VersionProfile.SplitArguments(version.Profile.Str("minecraftArguments")).Select(Expand));
            args.AddRange(game);
            args.AddRange(JvmArguments.Parse(options.ExtraGameArguments, "游戏"));
            var start = new ProcessStartInfo(java) { WorkingDirectory = instance, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            foreach (var variable in new[] { "_JAVA_OPTIONS", "JAVA_TOOL_OPTIONS", "JDK_JAVA_OPTIONS", "CLASSPATH" }) start.Environment.Remove(variable);
            return new(start, natives, args);
        }
        catch { Directory.Delete(natives, true); throw; }
    }

    public static void ExtractNatives(string archivePath, string destination, IReadOnlyList<string> exclude)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        long expanded = 0;
        if (archive.Entries.Count > 10000) throw new InvalidDataException("原生库文件数量超出限制。");
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/') || exclude.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) continue;
            if ((entry.ExternalAttributes >> 16 & 0xf000) == 0xa000) throw new InvalidDataException("原生库包含符号链接。");
            expanded += entry.Length;
            if (expanded > 512L * 1024 * 1024) throw new InvalidDataException("原生库解压体积超出限制。");
            var target = SafePaths.Under(destination, name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }
}
