using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BlockDock.Core;

public sealed class MinecraftInstaller
{
    public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private readonly AppSettings settings;
    public DownloadClient Network { get; }
    private List<GameVersion>? versions;
    public MinecraftInstaller(AppSettings settings, DownloadClient network) { this.settings = settings; Network = network; }

    public async Task<IReadOnlyList<GameVersion>> GetVersionsAsync(CancellationToken token)
    {
        var manifest = await Network.GetJsonAsync(ManifestUrl, token);
        versions = manifest.Array("versions").Select(n => new GameVersion(n.Str("id"), n.Str("type"), n.Str("url"), n.Str("sha1") is { Length: > 0 } sha ? sha : null,
            DateTimeOffset.TryParse(n.Str("releaseTime"), out var date) ? date : DateTimeOffset.MinValue)).ToList();
        return versions;
    }

    public async Task<JsonObject> GetVersionJsonAsync(string version, CancellationToken token)
    {
        var all = versions ?? (await GetVersionsAsync(token)).ToList();
        var entry = all.FirstOrDefault(v => v.Id == version) ?? throw new InvalidDataException($"官方列表中不存在 Minecraft {version}。");
        var cache = SafePaths.Under(settings.GameDirectory, $".blockdock/cache/metadata/{SafePaths.InstanceName(version)}.json");
        await Network.DownloadAsync(new(cache, [entry.Url], Sha1: entry.Sha1), token);
        return JsonNode.Parse(await File.ReadAllTextAsync(cache, token)) as JsonObject ?? throw new InvalidDataException("无效的版本元数据。");
    }

    public async Task<IReadOnlyList<LoaderVersion>> GetLoaderVersionsAsync(LoaderKind loader, string minecraft, CancellationToken token)
    {
        if (loader == LoaderKind.Vanilla) return [];
        if (loader == LoaderKind.Fabric)
        {
            var data = JsonNode.Parse(await Network.GetTextAsync($"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(minecraft)}", token)) as JsonArray ?? [];
            return data.Select(n => n?["loader"]).Where(n => n is not null).Select(n => new LoaderVersion(n.Str("version"), n.Str("version"), n?["stable"]?.GetValue<bool>() ?? false)).ToList();
        }
        async Task<IReadOnlyList<LoaderVersion>> ForgeApiAsync()
        {
            var data = JsonNode.Parse(await Network.GetTextAsync($"https://bmclapi2.bangbang93.com/forge/minecraft/{Uri.EscapeDataString(minecraft)}", token)) as JsonArray ?? [];
            return data.OfType<JsonObject>().Where(n => n.Array("files").Any(f => f.Str("category") == "installer" && f.Str("format") == "jar"))
                .Select(n => n.Str("version") + (n.Str("branch").Length > 0 ? "-" + n.Str("branch") : ""))
                .Select(v => new LoaderVersion(v, minecraft + "-" + v)).OrderByDescending(v => NumericVersion(v.Version), StringComparer.Ordinal).ToList();
        }
        if (loader == LoaderKind.Forge && settings.Source == DownloadSource.Mirror) return await ForgeApiAsync();
        var neoLegacy = loader == LoaderKind.NeoForge && minecraft == "1.20.1";
        var baseUrl = loader == LoaderKind.Forge ? "https://maven.minecraftforge.net/net/minecraftforge/forge/" :
            neoLegacy ? "https://maven.neoforged.net/releases/net/neoforged/forge/" : "https://maven.neoforged.net/releases/net/neoforged/neoforge/";
        XDocument xml;
        try { xml = XDocument.Parse(await Network.GetTextAsync(baseUrl + "maven-metadata.xml", token)); }
        catch (IOException) when (loader == LoaderKind.Forge && settings.Source != DownloadSource.Official) { return await ForgeApiAsync(); }
        var ids = xml.Descendants("version").Select(e => e.Value).Where(v => loader == LoaderKind.Forge || neoLegacy ? v.StartsWith(minecraft + "-", StringComparison.Ordinal) : NeoMatches(v, minecraft));
        var result = ids.Select(v => new LoaderVersion(loader == LoaderKind.Forge || neoLegacy ? v[(minecraft.Length + 1)..] : v, v,
            !v.Contains("beta", StringComparison.OrdinalIgnoreCase) && !v.Contains("alpha", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(v => NumericVersion(v.Version), StringComparer.Ordinal).ToList();
        if (result.Count == 0 && loader == LoaderKind.Forge && settings.Source != DownloadSource.Official) return await ForgeApiAsync();
        return result;
    }

    public static bool NeoMatches(string neo, string minecraft)
    {
        var parts = minecraft.Split('.');
        if (parts.Length >= 2 && parts[0] == "1" && int.TryParse(parts[1], out var minor) && minor >= 20)
        {
            var patch = parts.Length > 2 ? parts[2] : "0";
            return neo.StartsWith($"{minor}.{patch}.", StringComparison.Ordinal);
        }
        return parts.Length >= 2 && int.TryParse(parts[0], out var year) && year >= 26 && neo.StartsWith(minecraft + ".", StringComparison.Ordinal);
    }
    private static string NumericVersion(string version) => Regex.Replace(version, @"\d+", m => m.Value.PadLeft(10, '0'));

    public async Task<string> InstallAsync(InstallRequest request, IProgress<InstallProgress>? progress, CancellationToken token, PackInfo? pack = null)
    {
        var name = SafePaths.InstanceName(request.InstanceName);
        var root = Path.GetFullPath(settings.GameDirectory);
        Directory.CreateDirectory(SafePaths.Under(root, ".blockdock"));
        await using var installLock = new FileStream(SafePaths.Under(root, ".blockdock/install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var destination = SafePaths.Under(root, "versions/" + name);
        if (Directory.Exists(destination)) throw new IOException($"版本「{name}」已存在，请换一个名称。已有版本不会被覆盖。");
        // Stable work keys retain partial downloads and are separated by request / archive identity.
        var identity = JsonSerializer.Serialize(request) + (pack is null ? "" : pack.ArchivePath + File.GetLastWriteTimeUtc(pack.ArchivePath).Ticks + new FileInfo(pack.ArchivePath).Length);
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)))[..20];
        var work = SafePaths.Under(root, ".blockdock/staging/" + key);
        var instance = SafePaths.Under(work, "instance");
        Directory.CreateDirectory(instance);
        progress?.Report(new("读取版本信息", request.MinecraftVersion));
        var vanilla = await GetVersionJsonAsync(request.MinecraftVersion, token);
        JsonObject profile;
        if (request.Loader == LoaderKind.Vanilla)
        {
            await DownloadVanillaAsync(vanilla, instance, name, progress, token);
            profile = (JsonObject)vanilla.DeepClone();
            profile["id"] = name;
        }
        else
        {
            var baseName = SafePaths.InstanceName(request.MinecraftVersion);
            if (name == baseName) throw new ArgumentException("加载器版本名称需要与原版版本号不同，例如 1.20.1-Forge。");
            var baseDirectory = SafePaths.Under(root, "versions/" + baseName);
            // A base version is independently complete before the modded instance is committed.
            Directory.CreateDirectory(baseDirectory);
            await DownloadVanillaAsync(vanilla, baseDirectory, baseName, progress, token);
            var baseJson = SafePaths.Under(baseDirectory, baseName + ".json");
            if (!File.Exists(baseJson)) await WriteJsonAsync(baseJson, vanilla, token);
            var loaderVersion = request.LoaderVersion ?? throw new ArgumentException("请选择加载器版本。");
            if (request.Loader == LoaderKind.Fabric)
            {
                progress?.Report(new("安装 Fabric", loaderVersion));
                profile = await Network.GetJsonAsync($"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(baseName)}/{Uri.EscapeDataString(loaderVersion)}/profile/json", token);
                await Network.DownloadManyAsync(LibraryFiles(profile, root), "下载 Fabric 依赖", progress, token);
            }
            else profile = await new ForgeInstaller(settings, Network).InstallAsync(request, vanilla, work, progress, token);
            profile["id"] = name;
            profile["inheritsFrom"] = baseName;
        }
        if (pack is not null) await new ModpackInstaller(settings, Network).ApplyAsync(pack, instance, progress, token);
        token.ThrowIfCancellationRequested();
        progress?.Report(new("保存安装结果", name));
        await WriteJsonAsync(SafePaths.Under(instance, "blockdock-instance.json"), new JsonObject
        {
            ["name"] = name, ["minecraft"] = request.MinecraftVersion, ["loader"] = request.Loader.ToString(), ["loaderVersion"] = request.LoaderVersion,
            ["created"] = DateTimeOffset.Now.ToString("O"), ["modpack"] = pack?.Name, ["iconUrl"] = NormalizeIconUrl(pack?.IconUrl)
        }, token);
        // Let PCL enable per-version isolation for pack configs / mods.
        var pclDirectory = SafePaths.Under(instance, "PCL");
        Directory.CreateDirectory(pclDirectory);
        await File.WriteAllTextAsync(SafePaths.Under(pclDirectory, "Setup.ini"), "VersionArgumentIndie:1\r\n", token);
        await WriteJsonAsync(SafePaths.Under(instance, name + ".json"), profile, token);
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.Move(instance, destination);
        progress?.Report(new("安装完成", destination, 1, 1, Network.Bytes, Network.Speed));
        return destination;
    }

    private async Task DownloadVanillaAsync(JsonObject json, string instance, string id, IProgress<InstallProgress>? progress, CancellationToken token)
    {
        var root = settings.GameDirectory;
        var client = json["downloads"]?["client"] as JsonObject ?? throw new NotSupportedException("该版本没有可下载的 Java 版客户端。");
        progress?.Report(new("下载游戏本体", id));
        await Network.DownloadManyAsync([FromArtifact(SafePaths.Under(instance, id + ".jar"), client)], "下载游戏本体", progress, token);
        await Network.DownloadManyAsync(LibraryFiles(json, root), "下载游戏依赖", progress, token);
        if (json["logging"]?["client"]?["file"] is JsonObject logFile)
            await Network.DownloadAsync(FromArtifact(SafePaths.Under(root, "assets/log_configs/" + logFile.Str("id")), logFile), token);
        if (json["assetIndex"] is not JsonObject indexInfo) return;
        var indexPath = SafePaths.Under(root, "assets/indexes/" + indexInfo.Str("id") + ".json");
        progress?.Report(new("读取资源索引", indexInfo.Str("id")));
        await Network.DownloadAsync(FromArtifact(indexPath, indexInfo), token);
        var index = JsonNode.Parse(await File.ReadAllTextAsync(indexPath, token)) as JsonObject ?? throw new InvalidDataException("资源索引无效。");
        var assets = index.Object("objects");
        var files = assets.Select(kv =>
        {
            var hash = kv.Value.Str("hash");
            if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{40}$")) throw new InvalidDataException("资源哈希格式无效。");
            return new TransferFile(SafePaths.Under(root, "assets/objects/" + hash[..2] + "/" + hash), ["https://resources.download.minecraft.net/" + hash[..2] + "/" + hash], kv.Value.Long("size"), hash);
        }).ToList();
        await Network.DownloadManyAsync(files, "下载游戏资源", progress, token);
        bool legacy = index["virtual"]?.GetValue<bool>() == true;
        bool resources = index["map_to_resources"]?.GetValue<bool>() == true;
        if (legacy || resources)
        {
            foreach (var (path, data) in assets)
            {
                token.ThrowIfCancellationRequested();
                var hash = data.Str("hash");
                var source = SafePaths.Under(root, "assets/objects/" + hash[..2] + "/" + hash);
                foreach (var folder in (legacy ? new[] { SafePaths.Under(root, "assets/virtual/" + indexInfo.Str("id")) } : System.Array.Empty<string>()).Concat(resources ? [Path.Combine(instance, "resources")] : []))
                {
                    var target = SafePaths.Under(folder, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target, true);
                }
            }
        }
    }

    public static bool LibraryAllowed(JsonNode library)
    {
        if (library["rules"] is not JsonArray rules) return true;
        var allow = false;
        foreach (var rule in rules)
        {
            var os = rule?["os"];
            if (os is not null)
            {
                if (os.Str("name", "windows") != "windows") continue;
                if (os.Str("arch") is { Length: > 0 } arch && arch != (Environment.Is64BitOperatingSystem ? "x86_64" : "x86")) continue;
                if (os.Str("version") is { Length: > 0 } version && !Regex.IsMatch(Environment.OSVersion.Version.ToString(), version)) continue;
            }
            if (rule?["features"] is JsonObject features && features.Any(kv => kv.Value?.GetValue<bool>() == true)) continue;
            allow = rule.Str("action") == "allow";
        }
        return allow;
    }

    public static IEnumerable<TransferFile> LibraryFiles(JsonObject json, string root)
    {
        foreach (var library in json.Array("libraries").OfType<JsonObject>())
        {
            if (!LibraryAllowed(library)) continue;
            var downloads = library["downloads"] as JsonObject;
            if (downloads?["artifact"] is JsonObject artifact)
            {
                if (artifact.Str("url").Length > 0)
                    yield return FromArtifact(SafePaths.Under(root, "libraries/" + artifact.Str("path", SafePaths.Maven(library.Str("name")))), artifact);
            }
            else if (downloads is null && library.Str("name").Length > 0)
            {
                var path = SafePaths.Maven(library.Str("name"));
                yield return new(SafePaths.Under(root, "libraries/" + path), [library.Str("url", "https://libraries.minecraft.net/").TrimEnd('/') + "/" + path]);
            }
            var classifier = library?["natives"].Str("windows").Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
            if (!string.IsNullOrEmpty(classifier) && downloads?["classifiers"]?[classifier] is JsonObject native)
                yield return FromArtifact(SafePaths.Under(root, "libraries/" + native.Str("path")), native);
        }
    }

    public static TransferFile FromArtifact(string path, JsonObject artifact) => new(path, [artifact.Str("url")], artifact.Long("size"), artifact.Str("sha1") is { Length: > 0 } hash ? hash : null);
    public static async Task WriteJsonAsync(string path, JsonObject json, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), token);
        File.Move(temp, path, true);
    }

    public static IReadOnlyList<InstalledInstance> ListInstalled(string root)
    {
        var versionsDirectory = Path.Combine(root, "versions");
        if (!Directory.Exists(versionsDirectory)) return [];
        var result = new List<InstalledInstance>();
        foreach (var directory in Directory.GetDirectories(versionsDirectory))
        {
            var name = Path.GetFileName(directory);
            if (!File.Exists(Path.Combine(directory, name + ".json"))) continue;
            try
            {
                var profile = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, name + ".json"))) as JsonObject;
                if (profile is null) continue;
                var metadataPath = Path.Combine(directory, "blockdock-instance.json");
                JsonObject? metadata = null;
                try { if (File.Exists(metadataPath)) metadata = JsonNode.Parse(File.ReadAllText(metadataPath)) as JsonObject; }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { /* Valid profiles still appear when optional metadata is damaged. */ }
                var minecraft = String(metadata, "minecraft", String(profile, "inheritsFrom", name));
                var loader = String(metadata, "loader", InferLoader(profile).ToString());
                result.Add(new(name, minecraft, loader, directory, Directory.GetCreationTime(directory))
                { IconUrl = NormalizeIconUrl(String(metadata, "iconUrl")), PackName = String(metadata, "modpack") });
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException) { /* Other launchers can have unfinished profiles. */ }
        }
        return result.OrderByDescending(i => i.Created).ToList();
    }
    private static string String(JsonObject? node, string key, string fallback = "") => node?[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : fallback;
    private static string? NormalizeIconUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;
    private static LoaderKind InferLoader(JsonObject profile)
    {
        var libraries = profile.Array("libraries").OfType<JsonObject>().Select(library => String(library, "name")).ToList();
        if (libraries.Any(name => name.StartsWith("net.neoforged:", StringComparison.OrdinalIgnoreCase))) return LoaderKind.NeoForge;
        if (libraries.Any(name => name.StartsWith("net.fabricmc:fabric-loader:", StringComparison.OrdinalIgnoreCase)) || String(profile, "mainClass").Contains("fabricmc", StringComparison.OrdinalIgnoreCase)) return LoaderKind.Fabric;
        if (libraries.Any(name => name.StartsWith("net.minecraftforge:forge:", StringComparison.OrdinalIgnoreCase)) || String(profile, "mainClass").StartsWith("net.minecraftforge.", StringComparison.OrdinalIgnoreCase)) return LoaderKind.Forge;
        return LoaderKind.Vanilla;
    }
}
