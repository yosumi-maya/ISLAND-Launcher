using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BlockDock.Core;

public sealed class ForgeInstaller(AppSettings settings, DownloadClient network)
{
    public async Task<JsonObject> InstallAsync(InstallRequest request, JsonObject vanilla, string work, IProgress<InstallProgress>? progress, CancellationToken token)
    {
        var (installer, url) = InstallerPackage(request);
        progress?.Report(new("下载安装器", Path.GetFileName(installer)));
        string? hash = null;
        try { hash = (await network.GetTextAsync(url + ".sha1", token)).Trim().Split(' ', '\t', '\r', '\n')[0]; }
        catch (IOException) { /* Some historical repositories do not expose checksum sidecars. ZIP parsing still validates structure. */ }
        if (hash is not null && !Regex.IsMatch(hash, "^[a-fA-F0-9]{40}$")) throw new InvalidDataException("安装器校验码无效。");
        await network.DownloadAsync(new(installer, [url], Sha1: hash), token);
        using var zip = ZipFile.OpenRead(installer);
        var install = await ReadJsonAsync(zip, "install_profile.json", token);
        var expectedMinecraft = install.Str("minecraft", (install["install"]).Str("minecraft"));
        if (expectedMinecraft != request.MinecraftVersion) throw new InvalidDataException($"安装器需要 Minecraft {expectedMinecraft}，与所选版本不符。");
        if (install["spec"] is null) return await InstallLegacyAsync(zip, install, progress, token);
        var profile = await ReadJsonAsync(zip, install.Str("json", "/version.json").TrimStart('/'), token);
        var processors = ClientProcessors(install).ToList();
        JavaInstallation? java = null;
        if (processors.Count > 0)
        {
            var requirement = JavaRequirement.FromMetadata(vanilla, profile);
            progress?.Report(new("检测 Java", $"需要 64 位 Java {requirement.MinimumMajor} 或更高版本"));
            java = await JavaDiscovery.ResolveAsync(settings, requirement, token);
            progress?.Report(new("已选择 Java", java.Display));
        }
        var libraryRoot = SafePaths.Under(settings.GameDirectory, "libraries");
        Directory.CreateDirectory(libraryRoot);
        var dataDirectory = SafePaths.Under(work, "forge-data");
        Directory.CreateDirectory(dataDirectory);
        long extracted = 0;
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/')) continue;
            if (!entry.FullName.StartsWith("maven/", StringComparison.Ordinal) && !entry.FullName.StartsWith("data/", StringComparison.Ordinal)) continue;
            extracted += entry.Length;
            if (extracted > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("安装器解压体积异常。");
            var target = entry.FullName.StartsWith("maven/", StringComparison.Ordinal) ? SafePaths.Under(libraryRoot, entry.FullName[6..]) : SafePaths.Under(dataDirectory, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var input = entry.Open())
                await using (var output = File.Create(temporary))
                    await input.CopyToAsync(output, token);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, target, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        await network.DownloadManyAsync(MinecraftInstaller.LibraryFiles(install, settings.GameDirectory), "下载加载器安装依赖", progress, token);
        // Generated artifacts have empty download URLs and are produced by the processors below.
        await network.DownloadManyAsync(MinecraftInstaller.LibraryFiles(profile, settings.GameDirectory), "下载加载器运行依赖", progress, token);
        var resolve = Resolver(install, request, installer, libraryRoot, dataDirectory);
        var logFile = SafePaths.Under(work, "processors.log");
        await using var logWriter = new StreamWriter(logFile, append: true) { AutoFlush = true };
        for (int i = 0; i < processors.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var processor = processors[i];
            var processorJar = SafePaths.Under(libraryRoot, SafePaths.Maven(processor.Str("jar")));
            var args = processor.Array("args").Select(a => resolve(a!.GetValue<string>())).ToList();
            var outputs = processor.Object("outputs").Select(kv => (Path: resolve(kv.Key), Sha1: resolve(kv.Value!.GetValue<string>()))).ToList();
            async Task<bool> OutputsValidAsync()
            {
                if (outputs.Count == 0) return false;
                foreach (var output in outputs)
                    if (!await DownloadClient.ValidAsync(output.Path, new(output.Path, [], Sha1: output.Sha1), token)) return false;
                return true;
            }
            if (await OutputsValidAsync()) continue;
            progress?.Report(new("处理加载器文件", $"{i + 1}/{processors.Count} · {Path.GetFileName(processorJar)}", i, processors.Count));
            // Fetch official mappings through the same retry / mirror / hash pipeline.
            int mappingTask = args.IndexOf("DOWNLOAD_MOJMAPS");
            if (mappingTask >= 0 && vanilla["downloads"]?["client_mappings"] is JsonObject mappings &&
                args.IndexOf("--output") is var outputIndex && outputIndex >= 0 && outputIndex + 1 < args.Count)
                await network.DownloadAsync(MinecraftInstaller.FromArtifact(args[outputIndex + 1], mappings), token);
            else
            {
                using var jar = ZipFile.OpenRead(processorJar);
                using var reader = new StreamReader(jar.GetEntry("META-INF/MANIFEST.MF")?.Open() ?? throw new InvalidDataException("处理器缺少 Manifest。"));
                var manifest = (await reader.ReadToEndAsync(token)).Replace("\r\n ", "").Replace("\n ", "");
                var main = Regex.Match(manifest, @"(?m)^Main-Class:\s*([^\r\n]+)").Groups[1].Value.Trim();
                if (main.Length == 0) throw new InvalidDataException("处理器缺少 Main-Class。");
                var classpath = new[] { processorJar }.Concat(processor.Array("classpath").Select(c => SafePaths.Under(libraryRoot, SafePaths.Maven(c!.GetValue<string>()))));
                var command = new[] { "-Xmx2G", "-Djava.awt.headless=true", "-cp", string.Join(Path.PathSeparator, classpath), main }.Concat(args);
                object logGate = new();
                await JavaRunner.RunAsync(java!.Path, command, work, line => { lock (logGate) logWriter.WriteLine(line); }, token);
            }
            if (outputs.Count > 0 && !await OutputsValidAsync()) throw new InvalidDataException("加载器处理器输出校验失败，详情见 " + logFile);
        }
        foreach (var library in profile.Array("libraries").OfType<JsonObject>().Where(MinecraftInstaller.LibraryAllowed))
        {
            if (library["downloads"]?["artifact"] is not JsonObject generated || generated.Str("url").Length > 0) continue;
            var output = SafePaths.Under(libraryRoot, generated.Str("path", SafePaths.Maven(library.Str("name"))));
            if (!await ValidLocalLibraryAsync(MinecraftInstaller.FromArtifact(output, generated), token))
                throw new InvalidDataException("加载器未正确生成运行文件：" + output);
        }
        var generatedFiles = new JsonArray();
        foreach (var file in GeneratedLibraryFiles(install, libraryRoot, resolve))
        {
            if (!await ValidLocalLibraryAsync(file, token))
                throw new InvalidDataException("加载器未正确生成运行文件：" + file.Path);
            generatedFiles.Add(new JsonObject
            {
                ["path"] = Path.GetRelativePath(libraryRoot, file.Path).Replace('\\', '/'),
                ["sha1"] = file.Sha1
            });
        }
        profile["islandGeneratedLibraries"] = generatedFiles;
        return profile;
    }

    public async Task<IReadOnlyList<TransferFile>> ReadCachedGeneratedLibrariesAsync(InstallRequest request, CancellationToken token)
    {
        var (installer, _) = InstallerPackage(request);
        var sharedCache = !File.Exists(installer) && request.MinecraftVersion == "1.20.1";
        if (sharedCache)
            installer = SafePaths.Under(settings.GameDirectory, ".blockdock/cache/installers/" + Path.GetFileName(installer));
        if (!File.Exists(installer)) return [];
        try
        {
            using var archive = ZipFile.OpenRead(installer);
            var install = await ReadJsonAsync(archive, "install_profile.json", token);
            if (install["spec"] is null || install.Str("minecraft") != request.MinecraftVersion) return [];
            if (sharedCache)
            {
                var group = request.Loader == LoaderKind.NeoForge ? "net.neoforged" : "net.minecraftforge";
                var patched = install["data"]?["PATCHED"].Str("client");
                if (patched?.StartsWith($"[{group}:forge:{request.MinecraftVersion}-{request.LoaderVersion}:", StringComparison.Ordinal) != true)
                    return [];
            }
            var libraryRoot = SafePaths.Under(settings.GameDirectory, "libraries");
            var dataDirectory = SafePaths.Under(settings.GameDirectory, ".blockdock/cache");
            return GeneratedLibraryFiles(install, libraryRoot, Resolver(install, request, installer, libraryRoot, dataDirectory)).ToArray();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException) { return []; }
    }

    private (string Path, string Url) InstallerPackage(InstallRequest request)
    {
        var neo = request.Loader == LoaderKind.NeoForge;
        var artifact = neo && request.MinecraftVersion != "1.20.1" ? "neoforge" : "forge";
        var mavenVersion = artifact == "forge" ? request.MinecraftVersion + "-" + request.LoaderVersion : request.LoaderVersion!;
        var filename = $"{artifact}-{mavenVersion}-installer.jar";
        var maven = neo ? "https://maven.neoforged.net/releases/net/neoforged/" : "https://maven.minecraftforge.net/net/minecraftforge/";
        // Forge and NeoForge 1.20.1 publish some identical Maven versions under different groups.
        var cache = request.MinecraftVersion == "1.20.1" ? (neo ? "neoforge/" : "forge/") : "";
        return (SafePaths.Under(settings.GameDirectory, ".blockdock/cache/installers/" + cache + filename), $"{maven}{artifact}/{mavenVersion}/{filename}");
    }

    private static IEnumerable<JsonObject> ClientProcessors(JsonObject install)
        => install.Array("processors").OfType<JsonObject>().Where(p => p["sides"] is not JsonArray sides || sides.Any(s => s?.GetValue<string>() == "client"));

    private static IEnumerable<TransferFile> GeneratedLibraryFiles(JsonObject install, string libraryRoot, Func<string, string> resolve)
    {
        var outputs = ClientProcessors(install).SelectMany(processor => processor.Object("outputs"))
            .Select(pair => (Path: resolve(pair.Key), Sha1: (string?)resolve(pair.Value!.GetValue<string>())));
        var data = install.Object("data");
        // Forge and NeoForge can put runtime JARs in the data table without declaring processor outputs.
        foreach (var name in new[] { "PATCHED", "MC_EXTRA" })
        {
            if (data[name].Str("client").Length == 0) continue;
            var checksum = name + "_SHA";
            outputs = outputs.Append((resolve("{" + name + "}"),
                data[checksum].Str("client").Length > 0 ? resolve("{" + checksum + "}") : null));
        }
        var prefix = Path.TrimEndingDirectorySeparator(libraryRoot) + Path.DirectorySeparatorChar;
        return outputs.Where(output => Path.IsPathFullyQualified(output.Path))
            .Select(output => (Path: Path.GetFullPath(output.Path), output.Sha1))
            .Where(output => output.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(output => output.Sha1 is null)
            .DistinctBy(output => output.Path, StringComparer.OrdinalIgnoreCase)
            .Select(output => new TransferFile(SafePaths.Under(libraryRoot, Path.GetRelativePath(libraryRoot, output.Path)), [], Sha1: output.Sha1));
    }

    public static async Task<bool> ValidLocalLibraryAsync(TransferFile file, CancellationToken token)
    {
        if (!await DownloadClient.ValidAsync(file.Path, file, token)) return false;
        if (file.Sha1 is not null || file.Sha512 is not null ||
            !string.Equals(Path.GetExtension(file.Path), ".jar", StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            using var archive = ZipFile.OpenRead(file.Path);
            return archive.Entries.Count > 0;
        }
        catch (InvalidDataException) { return false; }
    }

    private Func<string, string> Resolver(JsonObject install, InstallRequest request, string installer, string libraryRoot, string dataDirectory)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SIDE"] = "client", ["ROOT"] = Path.GetFullPath(settings.GameDirectory), ["LIBRARY_DIR"] = libraryRoot,
            ["MINECRAFT_JAR"] = SafePaths.Under(settings.GameDirectory, $"versions/{request.MinecraftVersion}/{request.MinecraftVersion}.jar"),
            ["MINECRAFT_VERSION"] = request.MinecraftVersion, ["INSTALLER"] = installer
        };
        foreach (var (key, value) in install.Object("data"))
        {
            var raw = value.Str("client");
            data[key] = raw.StartsWith('/') ? SafePaths.Under(dataDirectory, raw.TrimStart('/')) : ResolveLiteral(raw);
        }
        string ResolveLiteral(string value) => value switch
        {
            ['[', .., ']'] => SafePaths.Under(libraryRoot, SafePaths.Maven(value[1..^1])),
            ['\'', .., '\''] => value[1..^1],
            _ => value
        };
        // Expand tokens from the installer metadata once so brackets and braces in real paths stay literal.
        return value => value is ['[', .., ']'] or ['\'', .., '\''] ? ResolveLiteral(value) :
            Regex.Replace(value, @"\{([A-Z0-9_]+)\}", m => data.TryGetValue(m.Groups[1].Value, out var replacement)
                ? replacement : throw new InvalidDataException("未知的安装器变量：" + m.Value));
    }

    private async Task<JsonObject> InstallLegacyAsync(ZipArchive zip, JsonObject install, IProgress<InstallProgress>? progress, CancellationToken token)
    {
        var profile = install["versionInfo"] as JsonObject ?? throw new NotSupportedException("不支持此旧版 Forge 安装器格式。");
        var path = SafePaths.Maven((install["install"]).Str("path"));
        var entry = zip.GetEntry((install["install"]).Str("filePath")) ?? throw new InvalidDataException("旧版安装器缺少内嵌的 Forge 文件。");
        var target = SafePaths.Under(settings.GameDirectory, "libraries/" + path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using (var input = entry.Open())
        await using (var output = File.Create(target)) await input.CopyToAsync(output, token);
        var files = MinecraftInstaller.LibraryFiles(profile, settings.GameDirectory).Where(f => !string.Equals(f.Path, target, StringComparison.OrdinalIgnoreCase));
        await network.DownloadManyAsync(files, "下载 Forge 依赖", progress, token);
        return (JsonObject)profile.DeepClone();
    }

    private static async Task<JsonObject> ReadJsonAsync(ZipArchive zip, string path, CancellationToken token)
    {
        using var reader = new StreamReader(zip.GetEntry(path)?.Open() ?? throw new InvalidDataException("安装器缺少 " + path));
        return JsonNode.Parse(await reader.ReadToEndAsync(token)) as JsonObject ?? throw new InvalidDataException("安装器 JSON 无效。");
    }
}
