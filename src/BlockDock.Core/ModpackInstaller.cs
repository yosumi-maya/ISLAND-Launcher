using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BlockDock.Core;

public sealed class ModpackInstaller(AppSettings settings, DownloadClient network)
{
    private const long MaximumExpandedSize = 30L * 1024 * 1024 * 1024;
    private static readonly HashSet<string> AllowedModrinthHosts = new(StringComparer.OrdinalIgnoreCase) { "cdn.modrinth.com", "github.com", "raw.githubusercontent.com", "gitlab.com" };

    public static async Task<PackInfo> InspectAsync(string archivePath, CancellationToken token)
    {
        var extension = Path.GetExtension(archivePath).ToLowerInvariant();
        if (extension is not ".zip" and not ".mrpack") throw new NotSupportedException("请拖入 .zip 或 .mrpack 整合包；RAR / 7z 请先解压并重新压缩为 ZIP。");
        using var zip = ZipFile.OpenRead(archivePath);
        if (zip.Entries.Count > 200000 || zip.Entries.Sum(e => e.Length) > MaximumExpandedSize) throw new InvalidDataException("整合包文件数或解压体积过大。");
        foreach (var entry in zip.Entries)
        {
            // Validate every entry, even ignored server overrides, before installation starts.
            if (entry.FullName.TrimEnd('/').Length > 0) _ = SafePaths.Under(Path.GetTempPath(), entry.FullName.TrimEnd('/'));
            if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000) throw new InvalidDataException("整合包不能包含符号链接。");
        }
        var manifestNames = new[] { "modrinth.index.json", "manifest.json", "mmc-pack.json", "mcbbs.packmeta" };
        foreach (var manifestName in manifestNames)
        {
            var entries = zip.Entries.Where(e => e.FullName == manifestName || e.FullName.EndsWith('/' + manifestName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (entries.Count == 0) continue;
            if (entries.Count != 1) throw new InvalidDataException("压缩包内包含多个整合包清单，请分别导入。");
            var entry = entries[0];
            var prefix = entry.FullName[..^manifestName.Length];
            var json = await ReadJsonAsync(entry, token);
            string minecraft;
            string name;
            LoaderKind loader = LoaderKind.Vanilla;
            string? loaderVersion = null;
            PackFormat format;
            int fileCount;
            int optional = 0;
            string overrides = json.Str("overrides", "overrides");
            if (manifestName == "modrinth.index.json")
            {
                if (json["formatVersion"]?.GetValue<int>() != 1 || json.Str("game") != "minecraft") throw new NotSupportedException("此 Modrinth 整合包格式版本或游戏不受支持。");
                format = PackFormat.Modrinth;
                var dependencies = json.Object("dependencies");
                minecraft = dependencies.Str("minecraft");
                name = json.Str("name", Path.GetFileNameWithoutExtension(archivePath));
                foreach (var (id, version) in dependencies)
                {
                    if (id == "minecraft") continue;
                    if (loader != LoaderKind.Vanilla) throw new NotSupportedException("整合包同时指定了多个加载器。");
                    loader = ParseLoader(id);
                    loaderVersion = version!.GetValue<string>();
                }
                fileCount = json.Array("files").Count(f => f?["env"].Str("client") != "unsupported");
                optional = json.Array("files").Count(f => f?["env"].Str("client") == "optional");
            }
            else if (manifestName == "manifest.json")
            {
                if (json.Str("manifestType") != "minecraftModpack" || json["manifestVersion"]?.GetValue<int>() != 1) throw new NotSupportedException("该 manifest.json 不是受支持的 CurseForge 整合包清单。");
                format = PackFormat.CurseForge;
                minecraft = (json["minecraft"]).Str("version");
                name = json.Str("name", Path.GetFileNameWithoutExtension(archivePath));
                var loaders = (json["minecraft"]).Array("modLoaders").OfType<JsonObject>().ToList();
                if (loaders.Count > 1) throw new NotSupportedException("整合包同时指定了多个加载器。");
                if (loaders.FirstOrDefault() is { } primary) (loader, loaderVersion) = ParseLoaderId(primary.Str("id"));
                fileCount = json.Array("files").Count;
                optional = json.Array("files").Count(f => f?["required"]?.GetValue<bool>() == false);
            }
            else if (manifestName == "mmc-pack.json")
            {
                format = PackFormat.MultiMC;
                var components = json.Array("components");
                minecraft = components.FirstOrDefault(c => c.Str("uid") == "net.minecraft").Str("version");
                name = Path.GetFileNameWithoutExtension(archivePath);
                var cfg = zip.GetEntry(prefix + "instance.cfg");
                if (cfg is not null)
                {
                    using var reader = new StreamReader(cfg.Open());
                    var text = await reader.ReadToEndAsync(token);
                    var match = Regex.Match(text, @"(?m)^name=(.+)$");
                    if (match.Success) name = match.Groups[1].Value.Trim();
                }
                foreach (var c in components)
                {
                    var uid = c.Str("uid");
                    if (uid is "net.minecraft" or "org.lwjgl" or "org.lwjgl3") continue;
                    if (loader != LoaderKind.Vanilla) throw new NotSupportedException("整合包同时指定了多个加载器或补丁。");
                    loader = uid switch { "net.minecraftforge" => LoaderKind.Forge, "net.neoforged" => LoaderKind.NeoForge, "net.fabricmc.fabric-loader" => LoaderKind.Fabric, _ => throw new NotSupportedException("暂不支持此整合包组件：" + uid) };
                    loaderVersion = c.Str("version");
                }
                overrides = zip.Entries.Any(e => e.FullName.StartsWith(prefix + ".minecraft/", StringComparison.Ordinal)) ? ".minecraft" : "minecraft";
                fileCount = zip.Entries.Count(e => e.FullName.StartsWith(prefix + overrides + "/mods/", StringComparison.Ordinal));
            }
            else
            {
                // MCBBS is commonly exported by HMCL and imported by PCL.
                format = PackFormat.MultiMC;
                name = json.Str("name", Path.GetFileNameWithoutExtension(archivePath));
                var addons = json.Array("addons");
                minecraft = addons.FirstOrDefault(c => c.Str("id") == "game").Str("version");
                foreach (var addon in addons.Where(c => c.Str("id") != "game"))
                {
                    if (loader != LoaderKind.Vanilla) throw new NotSupportedException("整合包同时指定了多个加载器。");
                    loader = ParseLoader(addon.Str("id"));
                    loaderVersion = addon.Str("version");
                }
                overrides = json.Str("overrides", "overrides");
                fileCount = zip.Entries.Count(e => e.FullName.StartsWith(prefix + overrides + "/mods/", StringComparison.Ordinal));
            }
            if (string.IsNullOrWhiteSpace(minecraft)) throw new InvalidDataException("整合包未声明 Minecraft 版本。");
            _ = SafePaths.InstanceName(minecraft);
            return new() { ArchivePath = archivePath, Name = name, MinecraftVersion = minecraft, Format = format, Loader = loader, LoaderVersion = loaderVersion,
                Prefix = prefix, Manifest = json, FileCount = fileCount, OptionalCount = optional, Overrides = overrides };
        }
        return await InspectRawAsync(archivePath, zip, token);
    }

    private static async Task<PackInfo> InspectRawAsync(string archivePath, ZipArchive zip, CancellationToken token)
    {
        var candidates = zip.Entries.Where(e => Regex.IsMatch(e.FullName, @"(^|/)versions/([^/]+)/\2\.json$")).ToList();
        var profiles = new List<(ZipArchiveEntry Entry, JsonObject Json)>();
        foreach (var entry in candidates) profiles.Add((entry, await ReadJsonAsync(entry, token)));
        var selected = profiles.Where(p => p.Json.Str("inheritsFrom").Length > 0).ToList();
        if (selected.Count == 0) selected = profiles;
        if (selected.Count != 1) throw new NotSupportedException("未识别到唯一的游戏版本。支持 Modrinth、CurseForge、MultiMC、MCBBS，以及含单个游戏版本的 .minecraft ZIP。");
        var (profileEntry, profile) = selected[0];
        var versionIndex = profileEntry.FullName.LastIndexOf("versions/", StringComparison.Ordinal);
        var prefix = profileEntry.FullName[..versionIndex];
        var loader = LoaderKind.Vanilla;
        string? loaderVersion = null;
        foreach (var library in profile.Array("libraries"))
        {
            var coordinate = library.Str("name").Split(':');
            if (coordinate.Length < 3) continue;
            if (coordinate[0] == "net.fabricmc" && coordinate[1] == "fabric-loader") { loader = LoaderKind.Fabric; loaderVersion = coordinate[2]; }
            if (coordinate[0] == "net.neoforged" && coordinate[1] is "neoforge" or "forge") { loader = LoaderKind.NeoForge; loaderVersion = coordinate[1] == "neoforge" ? coordinate[2] : StripMinecraftPrefix(coordinate[2]); }
            if (coordinate[0] == "net.minecraftforge" && coordinate[1] == "forge") { loader = LoaderKind.Forge; loaderVersion = StripMinecraftPrefix(coordinate[2]); }
        }
        var minecraft = profile.Str("inheritsFrom", profile.Str("id"));
        return new() { ArchivePath = archivePath, Name = Path.GetFileNameWithoutExtension(archivePath), MinecraftVersion = minecraft, Format = PackFormat.Raw,
            Prefix = prefix, Manifest = profile, Loader = loader, LoaderVersion = loaderVersion, FileCount = zip.Entries.Count(e => e.FullName.Contains("/mods/", StringComparison.Ordinal) || e.FullName.StartsWith("mods/", StringComparison.Ordinal)) };
    }

    public async Task ApplyAsync(PackInfo pack, string instance, IProgress<InstallProgress>? progress, CancellationToken token)
    {
        progress?.Report(new("解析整合包文件", pack.Name));
        if (pack.Format == PackFormat.Modrinth)
        {
            var files = new List<TransferFile>();
            foreach (var file in pack.Manifest.Array("files").OfType<JsonObject>())
            {
                var environment = file?["env"].Str("client");
                if (environment == "unsupported" || environment == "optional" && !settings.IncludeOptionalMods) continue;
                var urls = file.Array("downloads").Select(u => u!.GetValue<string>()).ToArray();
                if (urls.Length == 0 || urls.Any(u => !Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !AllowedModrinthHosts.Contains(uri.Host)))
                    throw new InvalidDataException("整合包文件包含非官方规范允许的下载域名：" + file.Str("path"));
                var sha1 = file?["hashes"].Str("sha1");
                var sha512 = file?["hashes"].Str("sha512");
                if (!Regex.IsMatch(sha1 ?? "", "^[a-fA-F0-9]{40}$") || !Regex.IsMatch(sha512 ?? "", "^[a-fA-F0-9]{128}$")) throw new InvalidDataException("整合包文件缺少有效的 SHA-1 / SHA-512 校验值。");
                files.Add(new(SafePaths.Under(instance, file.Str("path")), urls, file.Long("fileSize"), sha1, sha512));
            }
            await network.DownloadManyAsync(files, "下载整合包模组", progress, token);
        }
        else if (pack.Format == PackFormat.CurseForge)
        {
            if (string.IsNullOrWhiteSpace(settings.CurseForgeApiKey)) throw new InvalidOperationException("此 CurseForge 整合包需要 API Key。请在设置中填写自己的 CurseForge API Key 后重试。");
            var files = new List<TransferFile>();
            var entries = pack.Manifest.Array("files").OfType<JsonObject>().Where(f => settings.IncludeOptionalMods || f["required"]?.GetValue<bool>() != false).ToList();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var project = entry.Long("projectID") ?? throw new InvalidDataException("缺少 CurseForge projectID。");
                var fileId = entry.Long("fileID") ?? throw new InvalidDataException("缺少 CurseForge fileID。");
                progress?.Report(new("获取 CurseForge 下载地址", $"{i + 1}/{entries.Count}", i, entries.Count));
                var response = await network.GetJsonAsync($"https://api.curseforge.com/v1/mods/{project}/files/{fileId}", token, settings.CurseForgeApiKey);
                var data = response.Object("data");
                var url = data.Str("downloadUrl");
                if (url.Length == 0) throw new IOException($"模组作者未开放第三方下载（项目 {project}，文件 {fileId}）。请从 CurseForge 下载完整整合包后导入原生目录 ZIP。");
                var filename = SafePaths.InstanceName(data.Str("fileName"));
                var sha = data.Array("hashes").FirstOrDefault(h => h?["algo"]?.GetValue<int>() == 1).Str("value");
                if (sha.Length == 0) throw new InvalidDataException("CurseForge 没有提供该文件的 SHA-1 校验值。");
                files.Add(new(SafePaths.Under(instance, "mods/" + filename), [url], data.Long("fileLength"), sha));
            }
            await network.DownloadManyAsync(files, "下载 CurseForge 模组", progress, token);
        }
        using var zip = ZipFile.OpenRead(pack.ArchivePath);
        var layers = pack.Format switch
        {
            PackFormat.Modrinth => new[] { pack.Prefix + "overrides/", pack.Prefix + "client-overrides/" },
            PackFormat.Raw => new[] { pack.Prefix, RawVersionPrefix(pack) },
            _ => new[] { pack.Prefix + pack.Overrides.Trim('/') + "/" }
        };
        int done = 0;
        foreach (var layer in layers)
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(layer, StringComparison.Ordinal) && !e.FullName.EndsWith('/')))
        {
            token.ThrowIfCancellationRequested();
            var relative = entry.FullName[layer.Length..];
            if (relative.Length == 0 || pack.Format == PackFormat.Raw && relative.Equals(pack.Manifest.Str("id") + ".json", StringComparison.OrdinalIgnoreCase)) continue;
            var top = relative.Split('/')[0];
            if (pack.Format == PackFormat.Raw && (top is "versions" or "libraries" or "assets" or "logs" or "crash-reports" || top.Contains("account", StringComparison.OrdinalIgnoreCase) || top.Contains("launcher_profile", StringComparison.OrdinalIgnoreCase))) continue;
            if (relative.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && !relative.Contains('/') && pack.Format == PackFormat.Raw) continue;
            if (relative.Equals("blockdock-instance.json", StringComparison.OrdinalIgnoreCase) || relative.Equals(Path.GetFileName(instance) + ".json", StringComparison.OrdinalIgnoreCase)) continue;
            var target = SafePaths.Under(instance, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, token);
            progress?.Report(new("解压整合包配置", relative, ++done, zip.Entries.Count));
        }
    }

    private static string RawVersionPrefix(PackInfo pack) => pack.Prefix + "versions/" + pack.Manifest.Str("id") + "/";
    private static string StripMinecraftPrefix(string value) => Regex.Replace(value, @"^(?:1\.\d+(?:\.\d+)?|\d{2}\.\d+(?:\.\d+)?)-", "");
    private static LoaderKind ParseLoader(string id) => id switch
    {
        "forge" => LoaderKind.Forge, "neoforge" => LoaderKind.NeoForge, "fabric-loader" or "fabric" => LoaderKind.Fabric,
        _ => throw new NotSupportedException("暂不支持此加载器或依赖：" + id)
    };
    private static (LoaderKind, string) ParseLoaderId(string id)
    {
        foreach (var prefix in new[] { "neoforge-", "fabric-", "forge-" })
            if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return (ParseLoader(prefix.TrimEnd('-')), id[prefix.Length..]);
        throw new NotSupportedException("暂不支持此加载器：" + id);
    }
    private static async Task<JsonObject> ReadJsonAsync(ZipArchiveEntry entry, CancellationToken token)
    {
        if (entry.Length > 16 * 1024 * 1024) throw new InvalidDataException("整合包清单体积异常。");
        using var reader = new StreamReader(entry.Open());
        return JsonNode.Parse(await reader.ReadToEndAsync(token)) as JsonObject ?? throw new InvalidDataException("整合包清单不是 JSON 对象。");
    }
}
